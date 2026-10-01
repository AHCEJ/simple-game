namespace Games.Game;

public enum Tile : byte
{
    Grass = 0,
    Wall = 1,
    Water = 2,
    Road = 3,
    Tree = 4,
    Rock = 5,
}

public sealed class Player
{
    public required int Id { get; init; }
    public string Name { get; set; } = "Wanderer";
    public float X { get; set; }
    public float Y { get; set; }
    public int Dir { get; set; }
    public float Dx { get; set; }
    public float Dy { get; set; }

    public int Hp { get; set; } = 40;
    public int MaxHp { get; set; } = 40;
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int Gold { get; set; } = 0;
    public int Potions { get; set; } = 2;
    public bool Dead { get; set; }
    public float RespawnIn { get; set; }

    public int AttackCooldown { get; set; }
    public int PotionCooldown { get; set; }
    public int QuestIndex { get; set; } = -1;
    public int QuestProgress { get; set; }
    public bool QuestReady { get; set; }
    public int RespawnPoint { get; set; }
    public bool Online { get; set; } = true;

    public int Damage => 4 + Level * 2;
    public int DamageTaken => 3 + Level * 2;
}

public sealed class Mob
{
    public required int Id { get; init; }
    public int Kind { get; init; }
    public required string Name { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public int Damage { get; init; }
    public int Xp { get; init; }
    public int Gold { get; init; }
    public int AggroRange { get; init; }
    public float Speed { get; init; }
    public int TargetPlayer { get; set; } = -1;
    public int AttackCooldown { get; set; }
    public float RespawnIn { get; set; }
    public bool Dead { get; set; }
    public int HomeX { get; set; }
    public int HomeY { get; set; }
    public float WanderTimer { get; set; }
    public float WanderDx { get; set; }
    public float WanderDy { get; set; }
}

public sealed class Npc
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Emoji { get; init; }
    public int Kind { get; init; } // 0 = quest, 1 = shop
    public float X { get; set; }
    public float Y { get; set; }
    public int QuestId { get; set; } = -1;
    public int RespawnPoint { get; set; }
}

public sealed record QuestDef(
    string Title,
    string Goal,
    int MobKind,
    int Need,
    int XpReward,
    int GoldReward,
    string Intro);
