namespace AVAPitfall.Engine;

/// <summary>What the computer is after on the screen it's on.</summary>
public enum Aim { Exit, Treasure, LadderDown, Drop, LadderUp }

/// <summary>
/// The computer Harry, for the demonstration and the tests. It plans its way round the jungle like a map: across a
/// screen of jungle, three screens at a time through the tunnel where the walls allow, up and down the ladders, down
/// the holes. It heads for the nearest treasure left, then the next. On each screen it plays the crossing out on a
/// copy of the jungle before it commits: run for it, or jump now, or wait at the edge (jumping any log that comes)
/// until the crocodiles' jaws, the pit or the vine are right.
/// </summary>
public sealed class Pilot
{
    /// <summary>How far ahead a rehearsal runs, in steps.</summary>
    public const int Horizon = 9 * 60;

    private List<(int Screen, bool Under)> _route = [];
    private (int Screen, bool Under) _at = (-1, false);
    private bool _committed, _jumpFirst;
    /// <summary>How long it's been waiting; after a while a bump from a log is better than waiting on.</summary>
    private double _waited;
    private int _hopDir = 1;

    public string Doing { get; private set; } = "";
    public Aim Aim { get; private set; }
    public int Direction { get; private set; } = 1;

    public void Reset()
    {
        _route.Clear();
        _at = (-1, false);
        _committed = _jumpFirst = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var h = w.Harry;
        if (h.Pose is Pose.Dead or Pose.Sinking) { _committed = false; return default; }
        var here = (w.Screen, h.Under);
        // Halfway up a ladder is neither above ground nor below: keep to the plan until off it.
        if (h.Pose != Pose.Ladder && (here != _at || _route.Count == 0 || Done(w)))
        {
            _at = here;
            _committed = false;
            _waited = 0;
            Plan(w);
        }
        if (_committed)
        {
            var c = Go(w, _jumpFirst);
            _jumpFirst = false;
            return c;
        }
        // Rehearse: run for it, or jump now and run.
        foreach (bool jump in new[] { false, true })
        {
            if (jump && h.Pose != Pose.Ground) continue;
            if (Rehearse(w, jump, bumps: _waited > 5))
            {
                _waited = 0;
                _committed = true;
                Doing = Describe() + (jump ? " (jumping)" : "");
                return Go(w, jump);
            }
        }
        Doing = Describe() + ": waiting";
        _waited += Field.Step;
        return Wait(w);
    }

    private string Describe() => Aim switch
    {
        Aim.Exit => Direction > 0 ? "on to the right" : "on to the left",
        Aim.Treasure => "after the treasure",
        Aim.LadderDown => "down the ladder",
        Aim.Drop => "down a hole",
        _ => "up the ladder",
    };

    /// <summary>Whether the aim on this screen has been met (so a new one is needed).</summary>
    private bool Done(World w)
    {
        var s = w.Def;
        return Aim == Aim.Treasure && (s.Treasure < 0 || w.Taken[s.Treasure]);
    }

    // ───────────────────────────── the map ─────────────────────────────

    private static double Cost(ScreenDef s) =>
        4 + (s.Deadly ? (s.Vine ? 4 : 3) : 0) + s.Logs * 0.4 + (s.Thing is Thing.Fire or Thing.Cobra ? 0.3 : 0) + (s.Pit == Pit.Holes ? 0.5 : 0);

    /// <summary>Where each place leads, and how long it takes.</summary>
    public static IEnumerable<((int Screen, bool Under) To, double Cost)> Moves(JungleDef j, (int Screen, bool Under) at)
    {
        int n = j.Count;
        var s = j[at.Screen];
        if (!at.Under)
        {
            yield return (((at.Screen + 1) % n, false), Cost(s));
            yield return (((at.Screen - 1 + n) % n, false), Cost(s));
            if (s.Pit == Pit.Ladder) yield return ((at.Screen, true), 3);
            if (s.Pit == Pit.Holes) yield return ((at.Screen, true), 2.5);
        }
        else
        {
            foreach (int d in new[] { 1, -1 })
            {
                var to = j[at.Screen + d * JungleBook.UnderStride];
                if (s.Wall != d && to.Wall != -d) yield return (((to.Index), true), 4 + (s.Scorpion ? 1 : 0));
            }
            if (s.Pit == Pit.Ladder) yield return ((at.Screen, false), 3);
        }
    }

    /// <summary>Finds the way to the nearest treasure left, and sets the aim on this screen.</summary>
    private void Plan(World w)
    {
        var j = w.Jungle;
        var start = (w.Screen, w.Harry.Under);
        var dist = new Dictionary<(int, bool), double> { [start] = 0 };
        var from = new Dictionary<(int, bool), (int, bool)>();
        var queue = new PriorityQueue<(int, bool), double>();
        queue.Enqueue(start, 0);
        (int, bool)? goal = null;
        while (queue.TryDequeue(out var at, out double d))
        {
            if (d > dist[at]) continue;
            var s = j[at.Item1];
            if (!at.Item2 && s.Treasure >= 0 && !w.Taken[s.Treasure]) { goal = at; break; }
            foreach (var (to, c) in Moves(j, at))
            {
                double nd = d + c;
                if (dist.TryGetValue(to, out double old) && old <= nd) continue;
                dist[to] = nd;
                from[to] = at;
                queue.Enqueue(to, nd);
            }
        }
        _route = [];
        if (goal is { } g)
        {
            for (var p = g; p != start; p = from[p]) _route.Insert(0, p);
        }
        var here = j[w.Screen];
        if (_route.Count == 0)
        {
            // On the treasure's screen (or nothing left): go for it, or just keep going.
            Aim = here.Treasure >= 0 && !w.Taken[here.Treasure] && !w.Harry.Under ? Aim.Treasure : Aim.Exit;
            Direction = 1;
            return;
        }
        var next = _route[0];
        if (next.Under != w.Harry.Under)
        {
            Aim = w.Harry.Under ? Aim.LadderUp : here.Pit == Pit.Ladder ? Aim.LadderDown : Aim.Drop;
            // Which way to step off the ladder at the top: towards wherever comes after.
            if (Aim == Aim.LadderUp)
                _hopDir = _route.Count > 1 && _route[1].Screen == (w.Screen - 1 + j.Count) % j.Count ? -1 : 1;
            return;
        }
        Aim = Aim.Exit;
        int step = w.Harry.Under ? JungleBook.UnderStride : 1;
        Direction = next.Screen == (w.Screen + step) % j.Count ? 1 : -1;
    }

    // ───────────────────────────── on the screen ─────────────────────────────

    /// <summary>Where the aim is on this screen.</summary>
    private double Target(World w) => Aim switch
    {
        Aim.Exit => Direction > 0 ? Field.Width + 60 : -60,
        Aim.Treasure => JungleBook.ThingX,
        Aim.Drop => Sim.Holes(w.Def).OrderBy(x => Math.Abs(x - w.Harry.X)).First(),
        _ => Field.Mid,
    };

    /// <summary>The way across: run at the aim, jumping whatever's in the way at the last moment.</summary>
    private Controls Go(World w, bool jumpNow)
    {
        var h = w.Harry;
        var s = w.Def;
        switch (h.Pose)
        {
            case Pose.Ladder:
                if (Aim is Aim.LadderDown or Aim.Drop) return new Controls(Dy: 1);
                return h.Y <= Field.Path + 0.5 ? new Controls(Aim == Aim.LadderUp ? _hopDir : Math.Sign(Target(w) - Field.Mid)) : new Controls(Dy: -1);
            case Pose.Vine:
            {
                // Nothing to swing over: let go.
                if (!s.Deadly) return new Controls(Dy: 1);
                int d = Math.Sign(Target(w) - Field.Mid);
                double far = Field.Mid + d * JungleBook.VinePitHalf;
                var v = Sim.Vine(w.Clock);
                return (h.X - far) * d > -24 && v.Vx * d > 60 ? new Controls(Dy: 1) : default;
            }
            case Pose.Air:
                return default;
        }
        double target = Target(w);
        int dx = Math.Abs(target - h.X) < 4 ? 0 : Math.Sign(target - h.X);
        if (Aim == Aim.LadderDown && Math.Abs(h.X - Field.Mid) < 40) return new Controls(Dy: 1);
        if (Aim == Aim.LadderUp && Math.Abs(h.X - Field.Mid) < 20) return new Controls(Dy: -1);
        bool jump = jumpNow || dx != 0 && MustJump(w, dx);
        return new Controls(dx, 0, jump && !h.JumpPrev);
    }

    /// <summary>Something just ahead that needs jumping: a hole or pit at the next step, or a log, fire, cobra or scorpion close.</summary>
    private bool MustJump(World w, int dx)
    {
        var h = w.Harry;
        var s = w.Def;
        if (!h.Under)
        {
            bool Bad(Footing f) => f is Footing.Deadly or Footing.Jaws || f == Footing.Hole && Aim != Aim.Drop && Aim != Aim.LadderDown;
            // A pit or hole coming up: jump at the edge, or sooner if the landing is sure.
            for (double a = Harry.HalfWidth + 2; a <= 110; a += 4)
            {
                if (!Bad(Sim.At(s, h.X + dx * a, w.Clock))) continue;
                if (a <= Harry.HalfWidth + 6) return true;
                double land = h.X + dx * Sim.JumpLength, at = w.Clock + Sim.AirTime;
                // Not onto (or too close to jump) a fire or a cobra, and not onto a log.
                if (s.Thing is Thing.Fire or Thing.Cobra && Math.Abs(land - JungleBook.ThingX) < 54) return false;
                if (Sim.Logs(s, at).Any(lx => Math.Abs(land - lx) < Harry.HalfWidth + Sim.LogHalf + 6)) return false;
                return !Bad(Sim.At(s, land, at)) && !Bad(Sim.At(s, land + Harry.HalfWidth, at)) && !Bad(Sim.At(s, land - Harry.HalfWidth, at));
            }
            foreach (double lx in Sim.Logs(s, w.Clock))
            {
                double ahead = (lx - h.X) * dx, closing = Harry.Speed + (s.Rolling ? Sim.LogSpeed * dx : 0);
                if (ahead > 0 && ahead < 20 + closing * 0.17) return true;
            }
            if (s.Thing is Thing.Fire or Thing.Cobra)
            {
                double ahead = (JungleBook.ThingX - h.X) * dx;
                if (ahead > 0 && ahead < 20 + Harry.Speed * 0.17) return true;
            }
            return false;
        }
        if (s.Scorpion)
        {
            double ahead = (w.ScorpionX - h.X) * dx;
            if (ahead > 0 && ahead < 20 + (Harry.Speed + Sim.ScorpionSpeed) * 0.17) return true;
        }
        return false;
    }

    /// <summary>
    /// Waiting: at the edge of whatever deadly pit is between Harry and his aim (moving up to it if it's safe to), jumping
    /// any log or scorpion that comes at him.
    /// </summary>
    private Controls Wait(World w)
    {
        var h = w.Harry;
        var s = w.Def;
        if (h.Pose != Pose.Ground) return Go(w, false);
        int dx = 0;
        if (!h.Under && s.Deadly)
        {
            double half = s.Vine ? JungleBook.VinePitHalf : JungleBook.PitHalf;
            int side = Math.Sign(h.X - Field.Mid);
            double stage = Field.Mid + side * (half + 50);
            if ((Target(w) - Field.Mid) * side < 0 && Math.Abs(h.X - stage) > 4) dx = Math.Sign(stage - h.X);
        }
        bool jump = false;
        if (!h.Under)
            foreach (double lx in Sim.Logs(s, w.Clock))
            {
                double d = lx - h.X;
                double closing = s.Rolling && d > 0 ? Sim.LogSpeed : 0;
                if (closing > 0 && d < 20 + closing * 0.17 + Harry.HalfWidth && d > 0) jump = true;
            }
        else if (s.Scorpion && Math.Abs(w.ScorpionX - h.X) < 20 + Sim.ScorpionSpeed * 0.17 + Harry.HalfWidth) jump = true;
        if (dx != 0 && MustJump(w, dx)) dx = 0;
        return new Controls(dx, 0, jump && !h.JumpPrev);
    }

    /// <summary>Plays the crossing out on a copy: true if Harry gets where he's aiming without a scratch.</summary>
    public bool Rehearse(World w, bool jumpFirst, bool bumps = false)
    {
        var c = w.Copy();
        c.Probe = true;
        c.Cause = "";
        int taken = c.Taken.Count(t => t);
        bool under = c.Harry.Under;
        for (int i = 0; i < Horizon; i++)
        {
            Sim.Step(c, Go(c, jumpFirst && i == 0));
            var h = c.Harry;
            if (h.Pose is Pose.Dead or Pose.Sinking || c.Cause == "bumped" && !bumps) return false;
            switch (Aim)
            {
                case Aim.Exit when c.Exited: return true;
                case Aim.Treasure when c.Taken.Count(t => t) > taken: return true;
                case Aim.LadderDown or Aim.LadderUp when h.Pose == Pose.Ladder: return true;
                case Aim.Drop when h.Under && h.Pose == Pose.Ground: return true;
            }
            if (c.Exited) return false;
            if (!under && h.Under && Aim != Aim.Drop && Aim != Aim.LadderDown) return false;
        }
        return false;
    }
}
