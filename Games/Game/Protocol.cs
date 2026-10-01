using System.Text.Json;

namespace Games.Game;

public sealed record WelcomeMsg(
    string T,
    int Id,
    int W,
    int H,
    string Tiles,
    float X,
    float Y,
    string Name,
    long Tick);

public sealed record WirePlayer(int Id, string N, float X, float Y, int D, int H, int M, int L);
public sealed record WireNpc(int Id, string N, float X, float Y, string E, int K);
public sealed record WireMob(int Id, int K, float X, float Y, int H, int M);

public sealed record StateMsg(
    string T,
    long Tick,
    WirePlayer[] Players,
    WireNpc[] Npcs,
    WireMob[] Mobs);

public sealed record QuestView(int Id, string Name, string Goal, int Progress, int Need, bool Complete);

public sealed record Prompt(int Npc, string Text, string Act);

public sealed record YouMsg(
    string T,
    int Hp,
    int MaxHp,
    int Xp,
    int XpNeed,
    int Level,
    int Gold,
    int Potions,
    int Damage,
    bool Dead,
    float Respawn,
    QuestView? Quest,
    Prompt[] Prompts,
    int Online);

public sealed record ChatMsg(string T, string From, string Text, string Channel);
public sealed record LogMsg(string T, long Id, string Text);
public sealed record DamagedMsg(string T, int Entity, int Amount, string Target);

public sealed record ClientHello(string? T, string? N);
public sealed record ClientMove(string? T, float Dx, float Dy);
public sealed record ClientAct(string? T, string? Act, int? Id);
public sealed record ClientChat(string? T, string? Text);

public static class Proto
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
