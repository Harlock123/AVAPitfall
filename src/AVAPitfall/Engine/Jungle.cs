namespace AVAPitfall.Engine;

/// <summary>
/// The screen: the score and clock along the top, the jungle (canopy, trunks, the path) in the middle, the ground, and
/// the tunnel underneath it.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>Where Harry's feet are on the path, and on the tunnel floor; the tunnel's roof.</summary>
    public const double Path = 430, Floor = 680, Roof = 500;
    /// <summary>The middle of the screen, where the ladder and the pits are.</summary>
    public const double Mid = 512;
}

/// <summary>What fills the middle of the path.</summary>
public enum Pit
{
    None,
    /// <summary>A hole down to the tunnel, with a ladder.</summary>
    Ladder,
    /// <summary>Three holes down to the tunnel.</summary>
    Holes,
    Tar,
    Quicksand,
    Water,
}

/// <summary>What waits on the right of the path: a treasure, or something deadly.</summary>
public enum Thing { None, MoneyBag, SilverBar, GoldBar, Ring, Fire, Cobra }

/// <summary>One screen of jungle, and the stretch of tunnel under it.</summary>
public sealed class ScreenDef
{
    public int Index;
    public Pit Pit;
    /// <summary>The pit opens and closes (tar, quicksand or water).</summary>
    public bool Shifting;
    /// <summary>Crocodiles in the water: Harry can hop across on their heads while their jaws are shut.</summary>
    public bool Crocs;
    public bool Vine;
    /// <summary>Logs: how many, and whether they roll (to the left) or lie still.</summary>
    public int Logs;
    public bool Rolling;
    public Thing Thing;
    /// <summary>A brick wall in the tunnel: -1 at its left end, 1 at its right, 0 none.</summary>
    public int Wall;
    public bool Scorpion;
    /// <summary>Which treasure this is (index into the jungle's list), or -1.</summary>
    public int Treasure = -1;

    public bool Deadly => Pit is Pit.Tar or Pit.Quicksand or Pit.Water;
    public bool Hole => Pit is Pit.Ladder or Pit.Holes;
}

public sealed class JungleDef
{
    public ScreenDef[] Screens = [];
    /// <summary>The treasures: which screen each is on.</summary>
    public List<int> Treasures = [];
    public int Start;
    public int Count => Screens.Length;
    public ScreenDef this[int i] => Screens[((i % Count) + Count) % Count];
}

/// <summary>
/// The jungle: 128 screens in a ring, built from a seed the way the original was built from a shift register, each
/// with its pit, logs, vine, crocodiles, treasure or danger, and its stretch of tunnel. Walking a screen of tunnel
/// covers three of jungle.
/// </summary>
public static class JungleBook
{
    public const int Screens = 128, TreasureCount = 32, UnderStride = 3;

    /// <summary>The pits' width, and the holes and ladder's.</summary>
    public const double PitHalf = 200, VinePitHalf = 160, HoleHalf = 30;
    /// <summary>Where the thing on the right stands, the logs, the brick walls.</summary>
    public const double ThingX = 820, WallLeft = 110, WallRight = 914, WallHalf = 30;

    private static JungleDef? _jungle;
    private static readonly Lock Gate = new();

    public static JungleDef Jungle
    {
        get
        {
            lock (Gate) return _jungle ??= Build(1982);
        }
    }

    public static JungleDef Build(int seed)
    {
        var dice = new Dice(seed);
        var screens = new ScreenDef[Screens];
        for (int i = 0; i < Screens; i++)
        {
            var s = new ScreenDef { Index = i };
            int roll = dice.Next(100);
            if (roll < 18) s.Pit = Pit.None;
            else if (roll < 32) s.Pit = Pit.Ladder;
            else if (roll < 40) s.Pit = Pit.Holes;
            else if (roll < 56) s.Pit = Pit.Tar;
            else if (roll < 68) s.Pit = Pit.Quicksand;
            else s.Pit = Pit.Water;
            if (s.Deadly)
            {
                // Every deadly pit has a way across: it shifts, or there's a vine, or (in water) crocodiles.
                int way = dice.Next(s.Pit == Pit.Water ? 3 : 2);
                if (way == 0) s.Shifting = true;
                else if (way == 1) s.Vine = true;
                else s.Crocs = true;
                if (s.Pit == Pit.Water && s.Crocs && dice.Next(3) == 0) s.Vine = true;
            }
            else if (s.Pit == Pit.Ladder && dice.Next(4) == 0) s.Vine = true;
            if ((!s.Deadly || s.Shifting) && s.Pit != Pit.Holes)
            {
                s.Logs = dice.Next(4);
                // Rolling logs come in twos at most, close together, so one jump clears them.
                s.Rolling = s.Logs is > 0 and <= 2 && dice.Next(3) > 0;
            }
            if (dice.Next(4) == 0) s.Thing = dice.Next(2) == 0 ? Thing.Fire : Thing.Cobra;
            if (s.Pit != Pit.Ladder && dice.Next(10) < 3) s.Wall = dice.Next(2) == 0 ? -1 : 1;
            s.Scorpion = s.Wall == 0 && s.Pit != Pit.Ladder && dice.Next(3) == 0;
            screens[i] = s;
        }
        var jungle = new JungleDef { Screens = screens, Start = 0 };
        var start = screens[0];
        start.Pit = Pit.None;
        start.Logs = 1;
        start.Rolling = false;
        start.Thing = Thing.None;
        // The treasures: spread round the ring, never on the start, in place of whatever was there.
        var picks = new List<int>();
        for (int k = 0; k < TreasureCount; k++)
        {
            int at = (int)((k + 0.5) * Screens / TreasureCount) + dice.Next(3) - 1;
            at = Math.Clamp(at, 1, Screens - 1);
            while (picks.Contains(at)) at = at % (Screens - 1) + 1;
            picks.Add(at);
        }
        Thing[] kinds = [Thing.MoneyBag, Thing.SilverBar, Thing.GoldBar, Thing.Ring];
        foreach (int at in picks.OrderBy(p => p))
        {
            var s = screens[at];
            s.Thing = kinds[jungle.Treasures.Count % 4];
            s.Treasure = jungle.Treasures.Count;
            jungle.Treasures.Add(at);
        }
        return jungle;
    }

    public static int Points(Thing t) => t switch { Thing.MoneyBag => 2000, Thing.SilverBar => 3000, Thing.GoldBar => 4000, Thing.Ring => 5000, _ => 0 };

    public static string Name(Thing t) => t switch
    {
        Thing.MoneyBag => "MONEY BAG", Thing.SilverBar => "SILVER BAR", Thing.GoldBar => "GOLD BAR", Thing.Ring => "DIAMOND RING", _ => "",
    };
}
