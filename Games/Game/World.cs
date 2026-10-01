namespace Games.Game;

public sealed class World
{
    public const int W = 80;
    public const int H = 60;
    public const float TileSize = 1f;
    public const int AoiRadius = 26;
    public const int VisionTiles = 16;
    public const float LeashRange = 14f;
    public const float HomeReturnRange = 3f;

    private static readonly (string Name, string Emoji, int Hp, int Dmg, int Xp, int Gold, int Aggro, float Speed)[] MobDefs =
    {
        ("Green Slime", "s", 18, 3, 12, 3, 6, 1.6f),
        ("Wolf", "w", 26, 5, 20, 6, 9, 2.4f),
        ("Bandit", "b", 34, 7, 28, 10, 11, 2.0f),
        ("Ogre", "o", 60, 12, 55, 22, 12, 1.5f),
    };

    public static readonly QuestDef[] Quests =
    {
        new("Wipe Out Slimes", "Slay 6 slimes", 0, 6, 40, 15, "Slimes crawled out of the marsh again."),
        new("Wolf Trouble", "Slay 5 wolves", 1, 5, 80, 30, "Wolves are stalking the north road."),
        new("Bandit Camp", "Slay 4 bandits", 2, 4, 150, 60, "Bandits set up camp east of the gate."),
        new("Ogre in the Hills", "Slay 3 ogres", 3, 3, 300, 120, "An ogre towers over the north hills."),
    };

    private readonly Tile[] _tiles = new Tile[W * H];
    private readonly object _lock = new();
    private readonly Random _rng = new(1337);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public readonly Dictionary<int, Player> Players = new();
    public readonly List<Mob> Mobs = new();
    public readonly List<Npc> Npcs = new();
    public readonly List<ChatLine> ChatLog = new();
    public readonly List<(long Id, string Text)> Events = new();

    public sealed record ChatLine(long Id, string From, string Text, string Channel);

    private int _nextEntityId = 1;
    private long _eventId;
    private long _tick;

    public long Tick => _tick;
    public int PlayerCount { get { lock (_lock) return Players.Count; } }
    public int OnlineCount { get { lock (_lock) return Players.Count(p => p.Value.Online); } }

    public World()
    {
        GenerateMap();
        SpawnNpcs();
        SpawnMobs();
        AddChat("system", "Welcome to Aetheria. Press Enter to chat.");
    }

    public void Start()
    {
        _loop = Task.Run(async () =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                Step();
                try { await Task.Delay(50, _stopping.Token); }
                catch (OperationCanceledException) { break; }
            }
        });
    }

    public async Task StopAsync()
    {
        _stopping.Cancel();
        if (_loop is not null) await _loop;
    }

    private bool Walkable(Tile t) => t is Tile.Grass or Tile.Road;

    public Tile TileAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return Tile.Wall;
        return _tiles[y * W + x];
    }

    public void SetTile(int x, int y, Tile t)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        _tiles[y * W + x] = t;
    }

    private void GenerateMap()
    {
        for (int i = 0; i < _tiles.Length; i++) _tiles[i] = Tile.Grass;

        var r = new Random(7);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                bool edge = x < 2 || y < 2 || x >= W - 2 || y >= H - 2;
                if (edge) { _tiles[y * W + x] = Tile.Wall; continue; }

                double n = Math.Sin(x * 0.22) + Math.Cos(y * 0.19) + Math.Sin((x + y) * 0.11);
                if (n > 1.55) _tiles[y * W + x] = Tile.Water;
                else if (n < -1.75) _tiles[y * W + x] = Tile.Tree;
                else if (r.NextDouble() < 0.05) _tiles[y * W + x] = Tile.Rock;
            }
        }

        int townX = W / 2;
        int townY = H / 2;
        for (int y = townY - 6; y <= townY + 6; y++)
            for (int x = townX - 9; x <= townX + 9; x++)
                if (x > 0 && y > 0 && x < W - 1 && y < H - 1) _tiles[y * W + x] = Tile.Road;

        for (int i = 0; i < 26; i++)
        {
            int x = r.Next(3, W - 3);
            int y = r.Next(3, H - 3);
            if (TileAt(x, y) is Tile.Road) continue;
            _tiles[y * W + x] = Tile.Rock;
        }
    }

    private void SpawnNpcs()
    {
        int townX = W / 2;
        int townY = H / 2;
        for (int i = 0; i < Quests.Length; i++)
        {
            Npcs.Add(new Npc
            {
                Id = _nextEntityId++,
                Name = QuestGiverName(i),
                Emoji = "!",
                Kind = 0,
                QuestId = i,
                X = townX - 6 + i * 4,
                Y = townY - 4,
                RespawnPoint = townY,
            });
        }
        Npcs.Add(new Npc
        {
            Id = _nextEntityId++,
            Name = "Merchant",
            Emoji = "$",
            Kind = 1,
            X = townX + 8,
            Y = townY + 4,
        });
    }

    private static string QuestGiverName(int i) => i switch
    {
        0 => "Elder Mira",
        1 => "Ranger Kael",
        2 => "Captain Vorn",
        _ => "Sage Ilith",
    };

    private void SpawnMobs()
    {
        var r = new Random(99);
        int[] counts = { 16, 12, 9, 6 };
        for (int kind = 0; kind < counts.Length; kind++)
        {
            for (int i = 0; i < counts[kind]; i++)
            {
                var def = MobDefs[kind];
                int x = r.Next(4, W - 4);
                int y = r.Next(4, H - 4);
                if (TileAt(x, y) is not Tile.Grass) { i--; continue; }
                Mobs.Add(new Mob
                {
                    Id = _nextEntityId++,
                    Kind = kind,
                    Name = def.Name,
                    Hp = def.Hp,
                    MaxHp = def.Hp,
                    Damage = def.Dmg,
                    Xp = def.Xp,
                    Gold = def.Gold,
                    AggroRange = def.Aggro,
                    Speed = def.Speed,
                    X = x + 0.5f,
                    Y = y + 0.5f,
                    HomeX = x,
                    HomeY = y,
                    WanderDx = r.NextDouble() < 0.5 ? 1 : -1,
                    WanderDy = 0,
                });
            }
        }
    }

    public Player AddPlayer(string? name)
    {
        lock (_lock)
        {
            int townX = W / 2;
            int townY = H / 2;
            var p = new Player
            {
                Id = _nextEntityId++,
                Name = Sanitize(name),
                X = townX + 0.5f + (_rng.Next(-3, 4) * 0.5f),
                Y = townY + 0.5f + (_rng.Next(-2, 3) * 0.5f),
                RespawnPoint = townY,
            };
            Players[p.Id] = p;
            AddChat("system", $"{p.Name} enters the realm.");
            return p;
        }
    }

    public void RemovePlayer(int id)
    {
        lock (_lock)
        {
            if (Players.Remove(id, out var p)) AddChat("system", $"{p.Name} leaves the realm.");
            _lastEventSeen.Remove(id);
            _lastChatSeen.Remove(id);
        }
    }

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Wanderer";
        var trimmed = new string(name.Trim().Where(c => !char.IsControl(c)).Take(14).ToArray());
        return string.IsNullOrWhiteSpace(trimmed) ? "Wanderer" : trimmed;
    }

    public void SetInput(int id, float dx, float dy)
    {
        lock (_lock)
        {
            if (!Players.TryGetValue(id, out var p)) return;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 1e-4f)
            {
                p.Dx = dx / len;
                p.Dy = dy / len;
                p.Dir = p.Dx > 0.3f ? 0 : p.Dx < -0.3f ? 2 : p.Dy > 0.3f ? 1 : 3;
            }
            else
            {
                p.Dx = 0;
                p.Dy = 0;
            }
        }
    }

    public void Act(int id, string act, int targetId)
    {
        lock (_lock)
        {
            if (!Players.TryGetValue(id, out var p)) return;
            if (p.Dead) return;

            switch (act)
            {
                case "attack": Attack(p, targetId); break;
                case "potion": UsePotion(p); break;
                case "interact": Interact(p); break;
            }
        }
    }

    private void Attack(Player p, int targetId)
    {
        if (p.AttackCooldown > 0) return;
        p.AttackCooldown = 5;

        Mob? target = null;
        float best = float.MaxValue;
        foreach (var m in Mobs)
        {
            if (m.Dead) continue;
            float d = Dist(p.X, p.Y, m.X, m.Y);
            if (targetId != 0 && m.Id == targetId)
            {
                if (d <= 2.4f) { target = m; }
                break;
            }
            if (d < best && d <= 2.0f) { best = d; target = m; }
        }

        if (target is null) return;
        target.Hp -= p.Damage;
        PushEvent($"hit:{target.Id}:{p.Damage}:mob");

        if (target.Hp <= 0)
        {
            target.Dead = true;
            target.RespawnIn = 25 + _rng.Next(15);
            GrantXp(p, target);
            AddChat("system", $"{p.Name} defeated a {target.Name}.");
        }
    }

    private void GrantXp(Player p, Mob m)
    {
        p.Xp += m.Xp;
        p.Gold += m.Gold;
        while (p.Xp >= XpForLevel(p.Level))
        {
            p.Xp -= XpForLevel(p.Level);
            p.Level++;
            p.MaxHp += 12;
            p.Hp = p.MaxHp;
            AddChat("system", $"{p.Name} reached level {p.Level}!");
        }

        if (p.QuestIndex >= 0 && p.QuestIndex < Quests.Length && Quests[p.QuestIndex].MobKind == m.Kind)
        {
            p.QuestProgress++;
            if (p.QuestProgress >= Quests[p.QuestIndex].Need) p.QuestReady = true;
        }
    }

    public static int XpForLevel(int level) => 40 + level * level * 30;

    private void UsePotion(Player p)
    {
        if (p.Potions <= 0 || p.PotionCooldown > 0 || p.Hp >= p.MaxHp) return;
        p.Potions--;
        p.PotionCooldown = 20;
        p.Hp = Math.Min(p.MaxHp, p.Hp + 25);
    }

    private void Interact(Player p)
    {
        Npc? npc = null;
        float best = 2.5f;
        foreach (var n in Npcs)
        {
            float d = Dist(p.X, p.Y, n.X, n.Y);
            if (d < best) { best = d; npc = n; }
        }
        if (npc is null) { PushEvent("nothing"); return; }

        if (npc.Kind == 1)
        {
            if (p.Gold < 20) { PushEvent("nogold"); return; }
            p.Gold -= 20;
            p.Potions++;
            AddChat("system", $"{p.Name} buys a potion from {npc.Name}.");
            return;
        }

        int qi = npc.QuestId;
        if (qi < 0 || qi >= Quests.Length) return;

        if (p.QuestReady && p.QuestIndex == qi)
        {
            var q = Quests[qi];
            p.Gold += q.GoldReward;
            p.Xp += q.XpReward;
            while (p.Xp >= XpForLevel(p.Level))
            {
                p.Xp -= XpForLevel(p.Level);
                p.Level++;
                p.MaxHp += 12;
                p.Hp = p.MaxHp;
            }
            p.QuestReady = false;
            p.QuestProgress = 0;
            p.QuestIndex = qi + 1 < Quests.Length ? qi + 1 : -1;
            AddChat("system", $"{p.Name} completes {q.Title} (+{q.GoldReward}g).");
        }
        else if (p.QuestIndex == -1)
        {
            p.QuestIndex = qi;
            p.QuestProgress = 0;
            p.QuestReady = false;
            AddChat("system", $"{p.Name} accepts {Quests[qi].Title}.");
        }
    }

    public void Chat(int id, string text)
    {
        lock (_lock)
        {
            if (!Players.TryGetValue(id, out var p)) return;
            var clean = new string((text ?? "").Where(c => !char.IsControl(c)).Take(140).ToArray()).Trim();
            if (clean.Length == 0) return;
            AddChat(p.Name, clean);
        }
    }

    private void AddChat(string from, string text)
    {
        ChatLog.Add(new ChatLine(++_eventId, from, text, from == "system" ? "system" : "chat"));
        if (ChatLog.Count > 60) ChatLog.RemoveAt(0);
    }

    public List<ChatLine> NewChatFor(int playerId)
    {
        lock (_lock)
        {
            long since = _lastChatSeen.TryGetValue(playerId, out var l) ? l : 0;
            var fresh = ChatLog.Where(c => c.Id > since).ToList();
            if (ChatLog.Count > 0) _lastChatSeen[playerId] = ChatLog[^1].Id;
            return fresh;
        }
    }

    public List<ChatLine> ChatHistory() => new(ChatLog);

    public void PushEvent(string text)
    {
        Events.Add((++_eventId, text));
        if (Events.Count > 200) Events.RemoveRange(0, Events.Count - 200);
    }

    public static float Dist(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public void Step()
    {
        lock (_lock)
        {
            _tick++;
            float dt = 0.05f;

            foreach (var p in Players.Values)
            {
                if (p.AttackCooldown > 0) p.AttackCooldown--;
                if (p.PotionCooldown > 0) p.PotionCooldown--;

                if (p.Dead)
                {
                    p.RespawnIn -= dt;
                    if (p.RespawnIn <= 0)
                    {
                        p.Dead = false;
                        p.Hp = p.MaxHp;
                        p.X = W / 2 + 0.5f;
                        p.Y = H / 2 + 0.5f;
                        p.Dx = p.Dy = 0;
                        AddChat("system", $"{p.Name} respawns in town.");
                    }
                    continue;
                }

                if (p.Dx == 0 && p.Dy == 0) continue;
                float speed = 3.2f * dt;
                TryMove(p, p.X + p.Dx * speed, p.Y + p.Dy * speed);
            }

            foreach (var m in Mobs)
            {
                if (m.Dead)
                {
                    m.RespawnIn -= dt;
                    if (m.RespawnIn <= 0) RespawnMob(m);
                    continue;
                }

                if (m.AttackCooldown > 0) m.AttackCooldown--;

                Player? target = null;
                if (m.TargetPlayer != 0 && Players.TryGetValue(m.TargetPlayer, out var tp) && !tp.Dead)
                {
                    if (Dist(tp.X, tp.Y, m.X, m.Y) <= (float)m.AggroRange + 2.5f) target = tp;
                }

                if (target is null)
                {
                    float best = m.AggroRange;
                    foreach (var p in Players.Values)
                    {
                        if (p.Dead) continue;
                        float d = Dist(p.X, p.Y, m.X, m.Y);
                        if (d <= best) { best = d; target = p; }
                    }
                }
                m.TargetPlayer = target?.Id ?? 0;

                if (target is not null && Dist(m.X, m.Y, m.HomeX, m.HomeY) > LeashRange)
                {
                    target = null;
                    m.TargetPlayer = 0;
                }

                if (target is not null)
                {
                    float dx = target.X - m.X;
                    float dy = target.Y - m.Y;
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len > 0.001f)
                    {
                        float step = m.Speed * dt;
                        TryMoveMob(m, m.X + dx / len * step, m.Y + dy / len * step);
                    }
                    if (len <= 1.1f && m.AttackCooldown == 0)
                    {
                        m.AttackCooldown = 25;
                        target.Hp -= m.Damage;
                        PushEvent($"hit:{target.Id}:{m.Damage}:you");
                        if (target.Hp <= 0)
                        {
                            target.Hp = 0;
                            target.Dead = true;
                            target.RespawnIn = 4;
                            target.Dx = target.Dy = 0;
                            AddChat("system", $"{target.Name} was slain by a {m.Name}.");
                        }
                    }
                }
                else if (Dist(m.X, m.Y, m.HomeX, m.HomeY) > HomeReturnRange)
                {
                    float hx = m.HomeX + 0.5f - m.X;
                    float hy = m.HomeY + 0.5f - m.Y;
                    float hlen = MathF.Sqrt(hx * hx + hy * hy);
                    if (hlen > 0.001f)
                    {
                        float step = m.Speed * dt;
                        TryMoveMob(m, m.X + hx / hlen * step, m.Y + hy / hlen * step);
                    }
                }
                else
                {
                    m.WanderTimer -= dt;
                    if (m.WanderTimer <= 0)
                    {
                        m.WanderTimer = 1.5f + (float)_rng.NextDouble() * 2.5f;
                        var roll = _rng.Next(4);
                        m.WanderDx = roll == 0 ? 1 : roll == 1 ? -1 : 0;
                        m.WanderDy = roll == 2 ? 1 : roll == 3 ? -1 : 0;
                    }
                    TryMoveMob(m, m.X + m.WanderDx * m.Speed * 0.4f * dt, m.Y + m.WanderDy * m.Speed * 0.4f * dt);
                }
            }
        }
    }

    private void RespawnMob(Mob m)
    {
        for (int i = 0; i < 40; i++)
        {
            int x = _rng.Next(4, W - 4);
            int y = _rng.Next(4, H - 4);
            if (!Walkable(TileAt(x, y))) continue;
            m.X = x + 0.5f;
            m.Y = y + 0.5f;
            m.Hp = m.MaxHp;
            m.Dead = false;
            m.TargetPlayer = 0;
            m.HomeX = x;
            m.HomeY = y;
            return;
        }
        m.RespawnIn = 5;
    }

    private void TryMove(Player p, float nx, float ny)
    {
        if (Walkable(TileAt((int)MathF.Floor(nx), (int)MathF.Floor(p.Y)))) p.X = nx;
        if (Walkable(TileAt((int)MathF.Floor(p.X), (int)MathF.Floor(ny)))) p.Y = ny;
        p.X = Math.Clamp(p.X, 1, W - 1);
        p.Y = Math.Clamp(p.Y, 1, H - 1);
    }

    private bool TryMoveMob(Mob m, float nx, float ny)
    {
        bool moved = false;
        if (Walkable(TileAt((int)MathF.Floor(nx), (int)MathF.Floor(m.Y)))) { m.X = nx; moved = true; }
        if (Walkable(TileAt((int)MathF.Floor(m.X), (int)MathF.Floor(ny)))) { m.Y = ny; moved = true; }
        m.X = Math.Clamp(m.X, 1, W - 1);
        m.Y = Math.Clamp(m.Y, 1, H - 1);
        return moved;
    }

    public WelcomeMsg Welcome(Player p)
    {
        lock (_lock)
        {
            var chars = new char[W * H];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)('0' + (int)_tiles[i]);
            return new WelcomeMsg("welcome", p.Id, W, H, new string(chars), p.X, p.Y, p.Name, _tick);
        }
    }

    public StateMsg? BuildState(Player viewer, out List<(long Id, string Text)> newEvents)
    {
        lock (_lock)
        {
            newEvents = new();
            long since = _lastEventSeen.TryGetValue(viewer.Id, out var l) ? l : 0;
            foreach (var e in Events)
            {
                if (e.Id > since) newEvents.Add(e);
            }
            _lastEventSeen[viewer.Id] = _eventId;

            var players = new List<WirePlayer>();
            foreach (var p in Players.Values)
            {
                if (p.Dead && p.Id != viewer.Id) continue;
                if (Dist(viewer.X, viewer.Y, p.X, p.Y) > AoiRadius) continue;
                players.Add(new WirePlayer(p.Id, p.Name, p.X, p.Y, p.Dir, p.Hp, p.MaxHp, p.Level));
            }

            var npcs = new List<WireNpc>();
            foreach (var n in Npcs)
            {
                if (Dist(viewer.X, viewer.Y, n.X, n.Y) > AoiRadius) continue;
                npcs.Add(new WireNpc(n.Id, n.Name, n.X, n.Y, n.Emoji, n.Kind));
            }

            var mobs = new List<WireMob>();
            foreach (var m in Mobs)
            {
                if (m.Dead) continue;
                if (Dist(viewer.X, viewer.Y, m.X, m.Y) > AoiRadius) continue;
                mobs.Add(new WireMob(m.Id, m.Kind, m.X, m.Y, m.Hp, m.MaxHp));
            }

            return new StateMsg("state", _tick, players.ToArray(), npcs.ToArray(), mobs.ToArray());
        }
    }

    private readonly Dictionary<int, long> _lastEventSeen = new();
    private readonly Dictionary<int, long> _lastChatSeen = new();

    public YouMsg BuildYou(Player p)
    {
        lock (_lock)
        {
            var prompts = new List<Prompt>();
            foreach (var n in Npcs)
            {
                if (Dist(p.X, p.Y, n.X, n.Y) > 2.5f) continue;
                if (n.Kind == 1)
                {
                    prompts.Add(new Prompt(n.Id, "Potion - 20 gold", "buy"));
                }
                else if (n.QuestId >= 0 && n.QuestId < Quests.Length)
                {
                    var q = Quests[n.QuestId];
                    if (p.QuestReady && p.QuestIndex == n.QuestId)
                        prompts.Add(new Prompt(n.Id, $"Turn in {q.Title} (+{q.GoldReward}g)", "turnin"));
                    else if (p.QuestIndex == n.QuestId)
                        prompts.Add(new Prompt(n.Id, $"{q.Goal} ({p.QuestProgress}/{q.Need})", "progress"));
                    else
                        prompts.Add(new Prompt(n.Id, q.Title, "accept"));
                }
            }

            QuestView? qv = null;
            if (p.QuestIndex >= 0 && p.QuestIndex < Quests.Length)
            {
                var q = Quests[p.QuestIndex];
                qv = new QuestView(p.QuestIndex, q.Title, q.Goal, p.QuestProgress, q.Need, p.QuestReady);
            }

            return new YouMsg("you", p.Hp, p.MaxHp, p.Xp, XpForLevel(p.Level), p.Level, p.Gold, p.Potions, p.Damage,
                p.Dead, p.Dead ? Math.Max(0, p.RespawnIn) : 0, qv, prompts.ToArray(), OnlineCount);
        }
    }
}
