namespace AVAPitfall.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: left or right, up or down (up the screen is -1), and jump.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Jump = false);

public enum Pose { Ground, Air, Ladder, Vine, Sinking, Dead }

/// <summary>Pitfall Harry: where his feet are, which way he faces, and what he's doing.</summary>
public sealed class Harry
{
    public const double HalfWidth = 12, Height = 64, Speed = 300, JumpSpeed = 467, Gravity = 1555, ClimbSpeed = 140;
    public double X = 40, Y = Field.Path, Vx, Vy, Walk;
    public int Facing = 1;
    public Pose Pose = Pose.Ground;
    public bool JumpPrev, DownPrev;
    /// <summary>Underground, in the tunnel.</summary>
    public bool Under;
    /// <summary>Touching a log (points go while he is).</summary>
    public bool OnLog;
    public double SinkTime;
    /// <summary>Just let go of the vine: no catching hold of it again for a moment.</summary>
    public double VineCool;
    public Harry Copy() => (Harry)MemberwiseClone();
}

/// <summary>
/// Everything in the jungle that moves or changes, kept together so it can be copied whole: Harry, which screen he's
/// on, the scorpion, the treasures taken, the score and the clock. The logs, crocodiles, pits and vines go by the clock.
/// </summary>
public sealed class World
{
    public JungleDef Jungle = null!;
    public int Screen;
    public Harry Harry = new();
    public double ScorpionX = 800;
    public bool[] Taken = [];
    public int Score = 2000;
    /// <summary>Seconds played; the game lasts twenty minutes.</summary>
    public double Clock;
    public string Cause = "";
    /// <summary>Where Harry comes back after he's lost: the screen, above ground or under, and which end.</summary>
    public int SafeScreen;
    public bool SafeUnder;
    public int SafeSide = -1;
    /// <summary>For rehearsals: no points or sounds matter, and leaving the screen is the end of it.</summary>
    public bool Probe, Exited;
    public List<Sfx> Heard = [];

    public ScreenDef Def => Jungle[Screen];
    public int TreasuresLeft => Taken.Count(t => !t);
    public bool AllTaken => Taken.All(t => t);

    public World Copy() => new()
    {
        Jungle = Jungle, Screen = Screen, Harry = Harry.Copy(), ScorpionX = ScorpionX, Taken = [.. Taken], Score = Score, Clock = Clock,
        Cause = Cause, SafeScreen = SafeScreen, SafeUnder = SafeUnder, SafeSide = SafeSide, Probe = Probe, Exited = Exited,
    };
}
