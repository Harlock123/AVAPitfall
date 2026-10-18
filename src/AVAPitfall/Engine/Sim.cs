namespace AVAPitfall.Engine;

/// <summary>What's under Harry's feet on the path.</summary>
public enum Footing { Ground, Hole, Deadly, Croc, Jaws }

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double GameSeconds = 20 * 60;
    public const int LogPoints = 100, HolePoints = 100;
    /// <summary>A shifting pit's cycle: open, closing, shut, opening (seconds).</summary>
    public const double PitOpen = 2.4, PitMove = 0.9, PitShut = 2.2;
    public const double PitCycle = PitOpen + PitMove + PitShut + PitMove;
    /// <summary>The crocodiles' jaws: shut, then open.</summary>
    public const double JawsShut = 2.6, JawsOpen = 1.3;
    public const double CrocSpacing = 100, CrocHalf = 36;
    /// <summary>The vine: where it hangs from, how long, how far it swings (radians) and how long a swing takes.</summary>
    public const double VineX = Field.Mid, VineY = 110, VineLength = 250, VineSwing = 0.8, VinePeriod = 3.6;
    public const double LogSpeed = 250, LogHalf = 20, LogWrap = 1200, LogGap = 36;
    public const double ScorpionSpeed = 70, ScorpionHalf = 16;
    public const double HoleGap = 180;
    /// <summary>How long a jump is in the air, and so how far a running jump goes.</summary>
    public const double AirTime = 2 * Harry.JumpSpeed / Harry.Gravity, JumpLength = Harry.Speed * AirTime;
    /// <summary>Logs lying still: a pair close enough to clear in one jump, and one far off by itself.</summary>
    public static readonly double[] StillLogs = [230, 270, 880];

    public static void Start(World w, JungleDef jungle)
    {
        w.Jungle = jungle;
        w.Screen = jungle.Start;
        w.Harry = new Harry { X = 60, Y = Field.Path };
        w.Taken = new bool[jungle.Treasures.Count];
        w.Score = 2000;
        w.Clock = 0;
        SetSafe(w, -1);
        PlaceScorpion(w, -1);
    }

    // ───────────────────────────── things that go by the clock ─────────────────────────────

    /// <summary>How far each side of the middle the pit is open just now.</summary>
    public static double PitHalfWidth(ScreenDef s, double clock)
    {
        double full = s.Vine ? JungleBook.VinePitHalf : JungleBook.PitHalf;
        if (!s.Shifting) return full;
        double t = (clock + s.Index * 0.7) % PitCycle;
        if (t < PitOpen) return full;
        t -= PitOpen;
        if (t < PitMove) return full * (1 - t / PitMove);
        t -= PitMove;
        if (t < PitShut) return 0;
        t -= PitShut;
        return full * (t / PitMove);
    }

    public static bool JawsOpenAt(ScreenDef s, double clock) => (clock + s.Index * 0.3) % (JawsShut + JawsOpen) >= JawsShut;

    public static double CrocX(int k) => Field.Mid + (k - 1) * CrocSpacing;

    /// <summary>The vine's end, and how fast it's going across.</summary>
    public static (double X, double Y, double Vx) Vine(double clock)
    {
        double w = 2 * Math.PI / VinePeriod;
        double a = VineSwing * Math.Sin(w * clock), da = VineSwing * w * Math.Cos(w * clock);
        return (VineX + VineLength * Math.Sin(a), VineY + VineLength * Math.Cos(a), VineLength * Math.Cos(a) * da);
    }

    /// <summary>Where the screen's logs are just now.</summary>
    public static IEnumerable<double> Logs(ScreenDef s, double clock)
    {
        for (int k = 0; k < s.Logs; k++)
        {
            if (!s.Rolling) { yield return StillLogs[k]; continue; }
            double phase = k * LogGap + s.Index * 37;
            yield return Field.Width + 80 - (LogSpeed * clock + phase) % LogWrap;
        }
    }

    public static IEnumerable<double> Holes(ScreenDef s) => s.Pit switch
    {
        Pit.Ladder => [Field.Mid],
        // A running jump apart, so each can be jumped from the edge of the last.
        Pit.Holes => [Field.Mid - HoleGap, Field.Mid, Field.Mid + HoleGap],
        _ => [],
    };

    /// <summary>What's underfoot at x on the path just now.</summary>
    public static Footing At(ScreenDef s, double x, double clock)
    {
        foreach (double h in Holes(s))
            if (Math.Abs(x - h) < JungleBook.HoleHalf) return Footing.Hole;
        if (!s.Deadly) return Footing.Ground;
        double half = PitHalfWidth(s, clock);
        if (Math.Abs(x - Field.Mid) >= half) return Footing.Ground;
        if (s.Crocs)
            for (int k = 0; k < 3; k++)
                if (Math.Abs(x - CrocX(k)) < CrocHalf) return JawsOpenAt(s, clock) ? Footing.Jaws : Footing.Croc;
        return Footing.Deadly;
    }

    // ───────────────────────────── Harry ─────────────────────────────

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        var h = w.Harry;
        var s = w.Def;
        w.Clock += dt;
        bool jump = c.Jump && !h.JumpPrev, down = c.Dy > 0 && !h.DownPrev;
        h.JumpPrev = c.Jump;
        h.DownPrev = c.Dy > 0;
        if (h.VineCool > 0) h.VineCool -= dt;
        switch (h.Pose)
        {
            case Pose.Dead:
                return;
            case Pose.Sinking:
                h.SinkTime += dt;
                h.Y += 50 * dt;
                if (h.SinkTime > 1) h.Pose = Pose.Dead;
                return;
            case Pose.Ladder:
            {
                h.Y += c.Dy * Harry.ClimbSpeed * dt;
                if (c.Dy != 0) h.Walk += dt * 4;
                if (h.Y >= Field.Floor) { h.Y = Field.Floor; h.Under = true; h.Pose = Pose.Ground; }
                else if (h.Y <= Field.Path)
                {
                    h.Y = Field.Path;
                    h.Under = false;
                    // Off the top with a hop to one side, clear of the hole.
                    if (c.Dx != 0) { h.Pose = Pose.Air; h.Vx = c.Dx * Harry.Speed; h.Vy = -300; h.Facing = c.Dx; }
                }
                else h.Under = h.Y > Field.Roof;
                return;
            }
            case Pose.Vine:
            {
                var v = Vine(w.Clock);
                h.X = v.X;
                h.Y = v.Y + Harry.Height - 6;
                if (down || jump)
                {
                    h.Pose = Pose.Air;
                    h.Vx = v.Vx;
                    h.Vy = 0;
                    h.VineCool = 0.8;
                }
                return;
            }
            case Pose.Ground:
            {
                if (s.Pit == Pit.Ladder && Math.Abs(h.X - Field.Mid) < (h.Under ? 26 : 46) && (h.Under ? c.Dy < 0 : c.Dy > 0))
                {
                    h.X = Field.Mid;
                    h.Pose = Pose.Ladder;
                    if (!h.Under) h.Y = Field.Path + 4;
                    else h.Y = Field.Floor - 4;
                    return;
                }
                if (jump)
                {
                    h.Pose = Pose.Air;
                    h.Vy = -Harry.JumpSpeed;
                    h.Vx = c.Dx * Harry.Speed;
                    if (c.Dx != 0) h.Facing = c.Dx;
                    w.Heard.Add(Sfx.Jump);
                    break;
                }
                h.Vx = c.Dx * Harry.Speed;
                if (c.Dx != 0) { h.Facing = c.Dx; h.Walk += dt * 9; }
                h.X += h.Vx * dt;
                Walls(w);
                if (Leave(w)) return;
                if (!h.Under) Underfoot(w);
                break;
            }
            case Pose.Air:
            {
                h.Vy += Harry.Gravity * dt;
                h.X += h.Vx * dt;
                h.Y += h.Vy * dt;
                Walls(w);
                if (Leave(w)) return;
                if (!h.Under && s.Vine && h.Vy > -200 && h.VineCool <= 0)
                {
                    var v = Vine(w.Clock);
                    if (Math.Abs(h.X - v.X) < 30 && Math.Abs(h.Y - Harry.Height + 6 - v.Y) < 36)
                    {
                        h.Pose = Pose.Vine;
                        w.Heard.Add(Sfx.Yell);
                        return;
                    }
                }
                // Down a hole and into the tunnel.
                if (!h.Under && h.Y > Field.Path + 12)
                {
                    h.Under = true;
                    Lose(w, HolePoints);
                    w.Heard.Add(Sfx.Fall);
                }
                double ground = h.Under ? Field.Floor : Field.Path;
                if (h.Vy > 0 && h.Y >= ground)
                {
                    if (!h.Under && At(s, h.X, w.Clock) == Footing.Hole) break;
                    h.Y = ground;
                    h.Pose = Pose.Ground;
                    h.Vx = 0;
                    w.Heard.Add(Sfx.Land);
                    if (!h.Under) Underfoot(w);
                }
                break;
            }
        }
        Hazards(w);
    }

    /// <summary>What happens to Harry standing where he is on the path: a hole drops him, a pit or open jaws are the end.</summary>
    private static void Underfoot(World w)
    {
        var h = w.Harry;
        switch (At(w.Def, h.X, w.Clock))
        {
            case Footing.Hole:
                h.Pose = Pose.Air;
                h.Vx = 0;
                h.Vy = 0;
                break;
            case Footing.Deadly:
                h.Pose = Pose.Sinking;
                h.SinkTime = 0;
                w.Cause = w.Def.Pit switch { Pit.Tar => "the tar pit", Pit.Quicksand => "the quicksand", _ => "the water" };
                w.Heard.Add(Sfx.Sink);
                break;
            case Footing.Jaws:
                Die(w, "a crocodile");
                break;
        }
    }

    private static void Walls(World w)
    {
        var h = w.Harry;
        if (!h.Under || w.Def.Wall == 0) return;
        double wx = w.Def.Wall < 0 ? JungleBook.WallLeft : JungleBook.WallRight, reach = JungleBook.WallHalf + Harry.HalfWidth;
        if (Math.Abs(h.X - wx) >= reach) return;
        h.X = h.X < wx ? wx - reach : wx + reach;
        h.Vx = 0;
    }

    /// <summary>Off the edge of the screen and onto the next (three screens on, underground).</summary>
    private static bool Leave(World w)
    {
        var h = w.Harry;
        int dir = h.X < 0 ? -1 : h.X > Field.Width ? 1 : 0;
        if (dir == 0) return false;
        if (w.Probe)
        {
            w.Exited = true;
            h.X = Math.Clamp(h.X, 0, Field.Width);
            return true;
        }
        w.Screen = (w.Screen + dir * (h.Under ? JungleBook.UnderStride : 1) + w.Jungle.Count) % w.Jungle.Count;
        h.X -= dir * Field.Width;
        SetSafe(w, -dir);
        PlaceScorpion(w, -dir);
        w.Heard.Add(Sfx.Screen);
        return false;
    }

    private static void SetSafe(World w, int side)
    {
        w.SafeScreen = w.Screen;
        w.SafeUnder = w.Harry.Under;
        w.SafeSide = side;
    }

    /// <summary>The scorpion starts at the far end from where Harry came in.</summary>
    private static void PlaceScorpion(World w, int side) => w.ScorpionX = side < 0 ? 800 : 224;

    private static void Hazards(World w)
    {
        var h = w.Harry;
        var s = w.Def;
        if (h.Pose is Pose.Dead or Pose.Sinking) return;
        bool low = h.Y > (h.Under ? Field.Floor : Field.Path) - 34;
        if (!h.Under)
        {
            bool onLog = false;
            if (low && h.Pose != Pose.Ladder)
                foreach (double lx in Logs(s, w.Clock))
                    if (Math.Abs(h.X - lx) < Harry.HalfWidth + LogHalf) onLog = true;
            if (onLog && !h.OnLog)
            {
                Lose(w, LogPoints);
                w.Heard.Add(Sfx.Log);
            }
            h.OnLog = onLog;
            if (s.Thing is Thing.Fire or Thing.Cobra && low && Math.Abs(h.X - JungleBook.ThingX) < Harry.HalfWidth + 14)
                Die(w, s.Thing == Thing.Fire ? "the fire" : "a cobra");
            if (s.Treasure >= 0 && !w.Taken[s.Treasure] && h.Y > Field.Path - 80 && Math.Abs(h.X - JungleBook.ThingX) < Harry.HalfWidth + 22)
            {
                w.Taken[s.Treasure] = true;
                if (!w.Probe) w.Score += JungleBook.Points(s.Thing);
                w.Heard.Add(Sfx.Treasure);
            }
        }
        else if (s.Scorpion)
        {
            // The scorpion comes for him along the tunnel floor.
            double d = h.X - w.ScorpionX;
            if (Math.Abs(d) > 2) w.ScorpionX += Math.Sign(d) * ScorpionSpeed * Field.Step;
            if (low && Math.Abs(d) < Harry.HalfWidth + ScorpionHalf) Die(w, "the scorpion");
        }
    }

    private static void Lose(World w, int points)
    {
        if (w.Probe) { w.Cause = "bumped"; return; }
        w.Score = Math.Max(0, w.Score - points);
    }

    public static void Die(World w, string cause)
    {
        w.Harry.Pose = Pose.Dead;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }

    /// <summary>Back after being lost: dropping in at the end of the screen he last came in by.</summary>
    public static void Respawn(World w)
    {
        w.Screen = w.SafeScreen;
        var h = w.Harry = new Harry { Under = w.SafeUnder, Facing = -w.SafeSide };
        h.X = w.SafeSide < 0 ? 40 : Field.Width - 40;
        h.Y = (h.Under ? Field.Floor - 110 : Field.Path - 260);
        h.Pose = Pose.Air;
        PlaceScorpion(w, w.SafeSide);
    }
}
