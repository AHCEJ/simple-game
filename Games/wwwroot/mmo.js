(() => {
  const TILE = 32;
  const LERP = 0.22;

  const view = document.getElementById("view");
  const ctx = view.getContext("2d", { alpha: true });
  const overlay = document.getElementById("overlay");
  const statusEl = document.getElementById("status");
  const nameInput = document.getElementById("nameInput");
  const playBtn = document.getElementById("playBtn");
  const hud = document.getElementById("hud");
  const tips = document.getElementById("tips");
  const hpFill = document.getElementById("hpFill");
  const xpFill = document.getElementById("xpFill");
  const hpText = document.getElementById("hpText");
  const xpText = document.getElementById("xpText");
  const hpBar = hpFill.parentElement;
  const nameTag = document.getElementById("nameTag");
  const levelTag = document.getElementById("levelTag");
  const goldTag = document.getElementById("goldTag");
  const potionTag = document.getElementById("potionTag");
  const onlineTag = document.getElementById("onlineTag");
  const portraitLevel = document.getElementById("portraitLevel");
  const questBox = document.getElementById("questTracker");
  const questTitle = document.getElementById("questTitle");
  const questGoal = document.getElementById("questGoal");
  const questFill = document.getElementById("questFill");
  const floats = document.getElementById("floats");
  const log = document.getElementById("log");
  const chatInput = document.getElementById("chatInput");
  const flashEl = document.getElementById("flash");
  const mmWrap = document.getElementById("minimapWrap");
  const mmCanvas = document.getElementById("minimap");
  const mmCtx = mmCanvas.getContext("2d");
  const mmCoords = document.getElementById("mmCoords");

  const TILE_COLORS = ["#2f7d4f", "#4a5568", "#1d4ed8", "#8a7a5c", "#1f5133", "#5b6472"];

  const MOB_STYLE = [
    { color: "#4ade80", dark: "#22c55e", size: 13 },
    { color: "#b6bec9", dark: "#7b8494", size: 14 },
    { color: "#fbbf24", dark: "#d97706", size: 14 },
    { color: "#c084fc", dark: "#9333ea", size: 17 },
  ];

  let ws = null;
  let selfId = 0;
  let mapW = 80, mapH = 60, terrain = null;
  let me = { x: 40, y: 30, name: "?" };
  let dead = false;
  let chatFocused = false;
  let prevMove = { dx: 0, dy: 0 };
  let swingUntil = 0;
  let lastTickAt = performance.now();

  const ents = new Map();
  let terrainCanvas = null;

  function resize() {
    const dpr = window.devicePixelRatio || 1;
    view.width = Math.floor(window.innerWidth * dpr);
    view.height = Math.floor(window.innerHeight * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const mmSize = 176;
    mmCanvas.width = mmSize;
    mmCanvas.height = Math.round((mmSize * mapH) / mapW);
  }
  window.addEventListener("resize", resize);
  resize();

  function bakeTerrain() {
    terrainCanvas = document.createElement("canvas");
    terrainCanvas.width = mapW * TILE;
    terrainCanvas.height = mapH * TILE;
    const g = terrainCanvas.getContext("2d");
    for (let y = 0; y < mapH; y++) {
      for (let x = 0; x < mapW; x++) {
        const t = terrain.charCodeAt(y * mapW + x) - 48;
        g.fillStyle = TILE_COLORS[t] || TILE_COLORS[0];
        g.fillRect(x * TILE, y * TILE, TILE, TILE);
        if (t === 1) {
          g.fillStyle = "rgba(0,0,0,.28)";
          g.fillRect(x * TILE, y * TILE, TILE, 4);
          g.fillStyle = "rgba(255,255,255,.06)";
          g.fillRect(x * TILE, y * TILE + TILE - 2, TILE, 2);
        } else if (t === 4) {
          g.fillStyle = "#16402a";
          g.beginPath();
          g.arc(x * TILE + TILE / 2, y * TILE + TILE / 2, 8, 0, Math.PI * 2);
          g.fill();
          g.fillStyle = "#2f7d4f";
          g.beginPath();
          g.arc(x * TILE + TILE / 2 - 3, y * TILE + TILE / 2 - 3, 5, 0, Math.PI * 2);
          g.fill();
        } else if (t === 2) {
          g.fillStyle = "rgba(255,255,255,.08)";
          g.fillRect(x * TILE + 5, y * TILE + 6, TILE - 10, 2);
          g.fillRect(x * TILE + 4, y * TILE + 14, TILE - 8, 2);
        } else if (t === 5) {
          g.fillStyle = "#7c8797";
          g.fillRect(x * TILE + 8, y * TILE + 9, 14, 13);
          g.fillStyle = "#909bac";
          g.fillRect(x * TILE + 10, y * TILE + 11, 10, 9);
        } else if (t === 3) {
          g.fillStyle = "rgba(255,255,255,.05)";
          g.fillRect(x * TILE, y * TILE + TILE / 2 - 1, TILE, 2);
        }
      }
    }
  }

  const MM_SCALE = 6;

  function send(obj) {
    if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(obj));
  }

  function addLog(from, text, kind) {
    const line = document.createElement("div");
    if (kind === "system") {
      line.className = "sys";
      line.textContent = text;
    } else {
      const who = document.createElement("span");
      who.className = from === myName() ? "me" : "who";
      who.textContent = from + ": ";
      line.appendChild(who);
      line.appendChild(document.createTextNode(text));
    }
    log.appendChild(line);
    while (log.childElementCount > 60) log.removeChild(log.firstChild);
    log.scrollTop = log.scrollHeight;
  }

  const myName = () => me.name;

  function floatText(text, sx, sy, cls) {
    const el = document.createElement("div");
    el.className = "float " + cls;
    el.textContent = text;
    el.style.left = sx + "px";
    el.style.top = sy + "px";
    floats.appendChild(el);
    setTimeout(() => el.remove(), 1150);
  }

  const cam = { x: 0, y: 0 };

  function screenPos(x, y) {
    return {
      x: (x * TILE - cam.x) + view.clientWidth / 2,
      y: (y * TILE - cam.y) + view.clientHeight / 2,
    };
  }

  function shadow(x, y, rx) {
    ctx.fillStyle = "rgba(0,0,0,.35)";
    ctx.beginPath();
    ctx.ellipse(x, y + 12, rx, 4.5, 0, 0, Math.PI * 2);
    ctx.fill();
  }

  function drawBar(x, y, w, h, pct, color) {
    const p = Math.max(0, Math.min(1, pct));
    ctx.fillStyle = "rgba(0,0,0,.55)";
    ctx.beginPath();
    ctx.roundRect(x, y, w, h, h / 2);
    ctx.fill();
    if (p > 0) {
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.roundRect(x + 1, y + 1, Math.max(h - 2, (w - 2) * p), h - 2, (h - 2) / 2);
      ctx.fill();
    }
  }

  function drawPlayer(p, e, isMe) {
    const walk = e.moving ? Math.sin(performance.now() / 90) * 2 : 0;
    const y = p.y + walk;

    shadow(p.x, p.y, 11);

    const swing = e.id === selfId && performance.now() < swingUntil
      ? Math.sin(((swingUntil - performance.now()) / 180) * Math.PI)
      : 0;

    if (swing > 0) {
      ctx.save();
      ctx.translate(p.x, y);
      const ang = -0.9 + swing * 1.9;
      ctx.rotate(ang);
      const grd = ctx.createLinearGradient(0, -22, 0, -4);
      grd.addColorStop(0, "rgba(255,255,255,0)");
      grd.addColorStop(1, `rgba(255,240,190,${0.55 * swing})`);
      ctx.fillStyle = grd;
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.arc(0, 0, 24, -0.85, 0.85);
      ctx.closePath();
      ctx.fill();
      ctx.restore();
    }

    const body = isMe ? "#4cc2ff" : "#c8d2e0";
    const bodyDark = isMe ? "#1f7fb8" : "#7d8798";

    ctx.fillStyle = bodyDark;
    ctx.beginPath();
    ctx.roundRect(p.x - 7, y - 8, 14, 17, 6);
    ctx.fill();

    ctx.fillStyle = body;
    ctx.beginPath();
    ctx.roundRect(p.x - 5.5, y - 9, 11, 15, 5.5);
    ctx.fill();

    const eyeOff = [3, 0, -3, 0][e.dir || 0];
    const eyeY = e.dir === 1 ? 2 : e.dir === 3 ? -2 : 0;
    ctx.fillStyle = "#0b1220";
    ctx.beginPath();
    ctx.arc(p.x + eyeOff - 2.2, y - 2 + eyeY, 1.5, 0, Math.PI * 2);
    ctx.arc(p.x + eyeOff + 2.2, y - 2 + eyeY, 1.5, 0, Math.PI * 2);
    ctx.fill();

    if (isMe) {
      ctx.strokeStyle = "rgba(76,194,255,.55)";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.ellipse(p.x, p.y + 12, 14, 6, 0, 0, Math.PI * 2);
      ctx.stroke();
    }

    drawBar(p.x - 17, y - 24, 34, 5, e.hp / e.maxHp, e.hp / e.maxHp > 0.3 ? "#4ade80" : "#ef4444");

    ctx.font = "600 10px system-ui";
    ctx.textAlign = "center";
    ctx.fillStyle = "rgba(0,0,0,.75)";
    ctx.fillText(e.name, p.x + 1, y + 30);
    ctx.fillStyle = isMe ? "#9fdcff" : "#e2e8f0";
    ctx.fillText(e.name, p.x, y + 29);

    if (!isMe) {
      ctx.fillStyle = "#0b1220";
      ctx.beginPath();
      ctx.roundRect(p.x - 8, y + 33, 16, 12, 4);
      ctx.fill();
      ctx.strokeStyle = "rgba(255,255,255,.14)";
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.fillStyle = "#8fd8ff";
      ctx.font = "700 9px system-ui";
      ctx.fillText("L" + e.level, p.x, y + 42);
    }
  }

  function drawMob(p, e) {
    const st = MOB_STYLE[e.kindId] || MOB_STYLE[0];
    const hurt = e.hurtUntil && performance.now() < e.hurtUntil;
    const body = hurt ? "#ffffff" : st.color;
    const edge = hurt ? "#ffffff" : st.dark;

    shadow(p.x, p.y, st.size * 0.85);

    ctx.fillStyle = edge;
    ctx.beginPath();
    if (e.kindId === 1) {
      ctx.roundRect(p.x - st.size, p.y - st.size * 0.7, st.size * 2, st.size * 1.5, 7);
    } else {
      ctx.arc(p.x, p.y, st.size, 0, Math.PI * 2);
    }
    ctx.fill();

    ctx.fillStyle = body;
    ctx.beginPath();
    if (e.kindId === 1) {
      ctx.roundRect(p.x - st.size + 2, p.y - st.size * 0.7 + 2, st.size * 2 - 4, st.size * 1.5 - 4, 6);
    } else {
      ctx.arc(p.x, p.y, st.size - 2, 0, Math.PI * 2);
    }
    ctx.fill();

    ctx.fillStyle = "rgba(255,255,255,.28)";
    ctx.beginPath();
    ctx.arc(p.x - st.size * 0.32, p.y - st.size * 0.42, st.size * 0.3, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = hurt ? "#7f1d1d" : "#0f172a";
    const ex = st.size * 0.34;
    const ey = e.kindId === 0 ? 1 : 0;
    ctx.beginPath();
    ctx.arc(p.x - ex, p.y + ey, 2, 0, Math.PI * 2);
    ctx.arc(p.x + ex, p.y + ey, 2, 0, Math.PI * 2);
    ctx.fill();

    if (e.kindId === 3) {
      ctx.strokeStyle = edge;
      ctx.lineWidth = 2.5;
      ctx.beginPath();
      ctx.moveTo(p.x - 8, p.y - 12); ctx.lineTo(p.x - 13, p.y - 19);
      ctx.moveTo(p.x + 8, p.y - 12); ctx.lineTo(p.x + 13, p.y - 19);
      ctx.stroke();
    }

    drawBar(p.x - 15, p.y - st.size - 10, 30, 4, e.hp / e.maxHp, "#ef4444");
  }

  function drawNpc(p, e) {
    const merchant = e.kindId === 1;
    const color = merchant ? "#fbbf24" : "#f0abfc";
    shadow(p.x, p.y, 10);

    ctx.fillStyle = "rgba(0,0,0,.25)";
    ctx.beginPath();
    ctx.arc(p.x, p.y - 6, 11, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(p.x, p.y - 7, 10, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = "rgba(0,0,0,.4)";
    ctx.lineWidth = 1.5;
    ctx.stroke();

    ctx.fillStyle = "#0a0c10";
    ctx.font = "700 13px system-ui";
    ctx.textAlign = "center";
    ctx.fillText(e.emoji, p.x, p.y - 2);

    ctx.font = "600 10px system-ui";
    ctx.fillStyle = "rgba(0,0,0,.75)";
    ctx.fillText(e.name, p.x + 1, p.y + 19);
    ctx.fillStyle = merchant ? "#fcd34d" : "#f5d0fe";
    ctx.fillText(e.name, p.x, p.y + 18);
  }

  const promptFloats = [];

  function render() {
    if (!terrainCanvas) return;
    cam.x += (me.x * TILE - cam.x) * 0.3;
    cam.y += (me.y * TILE - cam.y) * 0.3;

    ctx.clearRect(0, 0, view.clientWidth, view.clientHeight);
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(terrainCanvas, 0, 0);

    const vw = view.clientWidth, vh = view.clientHeight;
    const drawn = [];

    for (const e of ents.values()) {
      const p = screenPos(e.rx, e.ry);
      if (p.x < -70 || p.y < -70 || p.x > vw + 70 || p.y > vh + 70) continue;
      drawn.push([e.ry, p, e]);
    }
    drawn.sort((a, b) => a[0] - b[0]);

    for (const [, p, e] of drawn) {
      if (e.type === "mob") drawMob(p, e);
      else if (e.type === "npc") drawNpc(p, e);
      else drawPlayer(p, e, e.id === selfId);
    }

    for (const f of promptFloats) {
      const p = screenPos(f.x, f.y);
      ctx.font = "600 12px system-ui";
      ctx.textAlign = "center";
      const w = ctx.measureText(f.text).width + 18;
      const bx = p.x - w / 2, by = p.y - 58;
      ctx.fillStyle = "rgba(10,14,20,.92)";
      ctx.beginPath();
      ctx.roundRect(bx, by, w, 22, 7);
      ctx.fill();
      ctx.strokeStyle = "rgba(76,194,255,.6)";
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.fillStyle = "#dff0ff";
      ctx.fillText(f.text, p.x, by + 15);
    }

    drawMinimap();

    mmCoords.textContent = Math.floor(me.x) + "," + Math.floor(me.y);

    if (dead) {
      ctx.fillStyle = "rgba(60,8,12,.55)";
      ctx.fillRect(0, 0, vw, vh);
      ctx.textAlign = "center";
      ctx.fillStyle = "#ff8a8a";
      ctx.font = "700 32px system-ui";
      ctx.fillText("You have fallen", vw / 2, vh / 2 - 8);
      ctx.fillStyle = "#c2a0a0";
      ctx.font = "14px system-ui";
      ctx.fillText("Respawning in town...", vw / 2, vh / 2 + 20);
    }
  }

  function drawMinimap() {
    const w = mmCanvas.width, h = mmCanvas.height;

    mmCtx.fillStyle = "#0c1118";
    mmCtx.fillRect(0, 0, w, h);
    mmCtx.imageSmoothingEnabled = false;

    const halfW = w / 2, halfH = h / 2;
    const srcW = w * TILE / MM_SCALE;
    const srcH = h * TILE / MM_SCALE;
    const ox = Math.round(me.x * TILE - srcW / 2);
    const oy = Math.round(me.y * TILE - srcH / 2);
    mmCtx.globalAlpha = 0.95;
    mmCtx.drawImage(terrainCanvas, ox, oy, srcW, srcH, 0, 0, w, h);
    mmCtx.globalAlpha = 1;

    const toX = (wx) => (wx * TILE - ox) / (TILE / MM_SCALE);
    const toY = (wy) => (wy * TILE - oy) / (TILE / MM_SCALE);

    for (const e of ents.values()) {
      const sx = toX(e.rx), sy = toY(e.ry);
      if (sx < -4 || sy < -4 || sx > w + 4 || sy > h + 4) continue;
      if (e.type === "mob") mmDot(sx, sy, "#ef4444", 2);
      else if (e.type === "npc") mmDot(sx, sy, "#f5c451", 2.2);
      else if (e.id !== selfId) mmDot(sx, sy, "#94a3b8", 2.2);
    }
    mmDot(w / 2, h / 2, "#4cc2ff", 3.4, "#0c1118");
  }

  function mmDot(x, y, color, r, ring) {
    if (ring) {
      mmCtx.fillStyle = ring;
      mmCtx.beginPath();
      mmCtx.arc(x, y, r + 1.5, 0, Math.PI * 2);
      mmCtx.fill();
    }
    mmCtx.fillStyle = color;
    mmCtx.beginPath();
    mmCtx.arc(x, y, r, 0, Math.PI * 2);
    mmCtx.fill();
  }

  function lerpEnts(dt) {
    const k = 1 - Math.pow(1 - LERP, dt / 16.67);
    for (const e of ents.values()) {
      const px = e.rx, py = e.ry;
      e.rx += (e.x - e.rx) * k;
      e.ry += (e.y - e.ry) * k;
      e.moving = Math.hypot(e.rx - px, e.ry - py) > 0.004;
    }
  }

  function upsert(id, type, apply) {
    let e = ents.get(id);
    const fresh = !e;
    if (fresh) {
      e = { type, rx: 0, ry: 0, moving: false };
      ents.set(id, e);
    }
    e.type = type;
    apply(e);
    if (fresh) { e.rx = e.x; e.ry = e.y; }
    return e;
  }

  function handle(msg) {
    switch (msg.t) {
      case "welcome": {
        selfId = msg.id;
        mapW = msg.w; mapH = msg.h;
        terrain = msg.tiles;
        me = { x: msg.x, y: msg.y, name: msg.name };
        bakeTerrain();
        resize();
        overlay.classList.add("hidden");
        hud.classList.remove("hidden");
        tips.classList.remove("hidden");
        mmWrap.classList.remove("hidden");
        break;
      }
      case "state": {
        const seen = new Set();
        for (const p of msg.players) {
          seen.add(p.id);
          if (p.id === selfId) { me.x = p.x; me.y = p.y; }
          upsert(p.id, "player", (e) => {
            e.x = p.x; e.y = p.y; e.name = p.n; e.hp = p.h; e.maxHp = p.m; e.level = p.l; e.dir = p.d;
          });
        }
        for (const n of msg.npcs) {
          seen.add(n.id);
          upsert(n.id, "npc", (e) => {
            e.x = n.x; e.y = n.y; e.name = n.n; e.emoji = n.e; e.kindId = n.k;
          });
        }
        for (const m of msg.mobs) {
          seen.add(m.id);
          upsert(m.id, "mob", (e) => {
            e.x = m.x; e.y = m.y; e.hp = m.h; e.maxHp = m.m; e.kindId = m.k;
          });
        }
        for (const [id, e] of ents) {
          if (!seen.has(id) && e.type !== "player") ents.delete(id);
        }
        break;
      }
      case "you": {
        const wasDead = dead;
        dead = msg.dead;
        const pct = Math.max(0, Math.min(1, msg.hp / msg.maxHp));
        hpFill.style.width = pct * 100 + "%";
        hpText.textContent = Math.max(0, msg.hp) + "/" + msg.maxHp;
        hpBar.classList.toggle("low", pct <= 0.3);

        const need = Math.max(1, msg.xpNeed || 1);
        xpFill.style.width = Math.min(100, (msg.xp / need) * 100) + "%";
        xpText.textContent = msg.xp + "/" + need + " XP";

        nameTag.textContent = me.name;
        levelTag.textContent = "Lv " + msg.level;
        portraitLevel.textContent = msg.level;
        goldTag.textContent = msg.gold + "g";
        potionTag.textContent = "Potion x" + msg.potions;
        onlineTag.innerHTML = "<b>" + (msg.online || 1) + "</b> online";

        if (msg.quest) {
          questBox.classList.remove("hidden");
          questBox.classList.toggle("ready", !!msg.quest.complete);
          questTitle.textContent = msg.quest.name + (msg.quest.complete ? " - ready!" : "");
          questGoal.textContent = msg.quest.goal + "  " + msg.quest.progress + "/" + msg.quest.need;
          questFill.style.width = Math.min(100, (msg.quest.progress / msg.quest.need) * 100) + "%";
        } else {
          questBox.classList.add("hidden");
        }

        promptFloats.length = 0;
        for (const pr of msg.prompts) {
          const found = ents.get(pr.npc);
          promptFloats.push({
            x: found ? found.x : me.x,
            y: found ? found.y : me.y,
            text: pr.text + "  [E]",
          });
        }

        if (wasDead !== dead && dead) hurtFlash();
        break;
      }
      case "chat": addLog(msg.from, msg.text, msg.channel); break;
      case "float": {
        const p = screenPos(me.x, me.y - 1);
        floatText(msg.text, p.x, p.y, "info");
        break;
      }
      case "dmg": {
        const e = ents.get(msg.entity);
        if (e) {
          if (msg.target === "you") {
            e.hp = Math.max(0, e.hp - msg.amount);
            e.hurtUntil = performance.now() + 160;
            hurtFlash();
            const p = screenPos(me.x, me.y - 0.8);
            floatText("-" + msg.amount, p.x, p.y, "dmg-you");
          } else {
            const p = screenPos(e.rx, e.ry - 0.7);
            floatText(String(msg.amount), p.x, p.y, "dmg");
          }
          if (msg.target !== "you" && msg.entity === selfId) swingUntil = performance.now() + 180;
        }
        break;
      }
    }
  }

  function hurtFlash() {
    flashEl.classList.remove("on");
    void flashEl.offsetWidth;
    flashEl.classList.add("on");
  }

  function nearestMob() {
    let best = null, bestD = Infinity;
    for (const e of ents.values()) {
      if (e.type !== "mob") continue;
      const d = Math.hypot(e.rx - me.x, e.ry - me.y);
      if (d < bestD) { bestD = d; best = e; }
    }
    return bestD <= 2.2 ? best : null;
  }

  const keys = new Set();
  function sendMove() {
    let dx = 0, dy = 0;
    if (keys.has("w") || keys.has("arrowup")) dy -= 1;
    if (keys.has("s") || keys.has("arrowdown")) dy += 1;
    if (keys.has("a") || keys.has("arrowleft")) dx -= 1;
    if (keys.has("d") || keys.has("arrowright")) dx += 1;
    if (dx === prevMove.dx && dy === prevMove.dy) return;
    prevMove = { dx, dy };
    send({ t: "move", dx, dy });
  }

  document.addEventListener("keydown", (e) => {
    const k = e.key.toLowerCase();

    if (k === "enter") {
      e.preventDefault();
      if (chatFocused) {
        const text = chatInput.value.trim();
        if (text) send({ t: "chat", text });
        chatInput.value = "";
        chatInput.blur();
        chatFocused = false;
      } else {
        chatFocused = true;
        chatInput.focus();
      }
      return;
    }

    if (chatFocused) return;
    if (k === "escape") { chatInput.blur(); chatFocused = false; return; }

    if (k === " " || k === "spacebar") {
      e.preventDefault();
      swingUntil = performance.now() + 180;
      const m = nearestMob();
      send({ t: "act", act: "attack", id: m ? m.id : 0 });
      return;
    }
    if (k === "e") { e.preventDefault(); send({ t: "act", act: "interact", id: 0 }); return; }
    if (k === "q") { e.preventDefault(); send({ t: "act", act: "potion", id: 0 }); return; }

    if (["w", "a", "s", "d", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(k)) {
      e.preventDefault();
      keys.add(k);
      sendMove();
    }
  });

  document.addEventListener("keyup", (e) => {
    const k = e.key.toLowerCase();
    if (keys.delete(k)) sendMove();
  });

  chatInput.addEventListener("blur", () => { chatFocused = false; });

  view.addEventListener("click", (e) => {
    if (chatFocused) return;
    const rect = view.getBoundingClientRect();
    const wx = (e.clientX - rect.left - view.clientWidth / 2 + cam.x) / TILE;
    const wy = (e.clientY - rect.top - view.clientHeight / 2 + cam.y) / TILE;
    let best = null, bestD = 1.5;
    for (const en of ents.values()) {
      if (en.type !== "mob") continue;
      const d = Math.hypot(en.rx - wx, en.ry - wy);
      if (d < bestD) { bestD = d; best = en; }
    }
    swingUntil = performance.now() + 180;
    send({ t: "act", act: "attack", id: best ? best.id : 0 });
  });

  function connect() {
    const name = nameInput.value.trim() || "Wanderer";
    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    statusEl.textContent = "Connecting...";
    statusEl.classList.remove("err");

    ws = new WebSocket(`${proto}//${location.host}/ws?name=${encodeURIComponent(name)}`);

    ws.addEventListener("open", () => { statusEl.textContent = "Connected"; });
    ws.addEventListener("message", (ev) => {
      try { handle(JSON.parse(ev.data)); } catch { }
    });
    ws.addEventListener("close", () => {
      if (overlay.classList.contains("hidden")) {
        statusEl.textContent = "Disconnected";
        statusEl.classList.add("err");
        overlay.classList.remove("hidden");
        hud.classList.add("hidden");
        tips.classList.add("hidden");
        mmWrap.classList.add("hidden");
      }
    });
    ws.addEventListener("error", () => { statusEl.textContent = "Connection failed"; statusEl.classList.add("err"); });
  }

  playBtn.addEventListener("click", connect);
  nameInput.addEventListener("keydown", (e) => { if (e.key === "Enter") connect(); });

  const saved = localStorage.getItem("aetheria.name");
  if (saved) nameInput.value = saved;
  nameInput.addEventListener("change", () => localStorage.setItem("aetheria.name", nameInput.value.trim()));

  function loop(now) {
    lerpEnts(Math.min(64, now - lastTickAt));
    render();
    requestAnimationFrame(loop);
  }
  lastTickAt = performance.now();
  requestAnimationFrame(loop);
})();