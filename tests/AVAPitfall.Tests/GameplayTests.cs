using Avalonia.Input;
using AVAPitfall.Engine;

namespace AVAPitfall.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapitfall-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A little jungle: plain screens but for the one given (at index 1), and a treasure on screen 5.</summary>
    private static JungleDef Little(Action<ScreenDef>? build = null)
    {
        var screens = Enumerable.Range(0, 9).Select(i => new ScreenDef { Index = i }).ToArray();
        build?.Invoke(screens[1]);
        screens[5].Thing = Thing.GoldBar;
        screens[5].Treasure = 0;
        return new JungleDef { Screens = screens, Treasures = [5] };
    }

    private static World In(JungleDef j, int screen = 1, double x = 100, bool under = false)
    {
        var w = new World();
        Sim.Start(w, j);
        w.Screen = screen;
        w.Harry = new Harry { X = x, Y = under ? Field.Floor : Field.Path, Under = under };
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static int Steps(double seconds) => (int)Math.Round(seconds * 60);

    // ───────────────────────────── the jungle ─────────────────────────────

    [Fact]
    public void The_jungle_is_the_same_from_the_same_seed_with_thirty_two_treasures_on_different_screens()
    {
        var a = JungleBook.Build(1982);
        var b = JungleBook.Build(1982);
        Assert.Equal(a.Screens.Select(s => (s.Pit, s.Vine, s.Crocs, s.Logs, s.Thing, s.Wall)), b.Screens.Select(s => (s.Pit, s.Vine, s.Crocs, s.Logs, s.Thing, s.Wall)));
        var j = JungleBook.Jungle;
        Assert.Equal(JungleBook.Screens, j.Count);
        Assert.Equal(32, j.Treasures.Distinct().Count());
        Assert.DoesNotContain(j.Start, j.Treasures);
        Assert.Equal(Pit.None, j[j.Start].Pit);
        Assert.All(j.Treasures, t => Assert.True(JungleBook.Points(j[t].Thing) > 0));
        Assert.Equal(2000 + 8 * (2000 + 3000 + 4000 + 5000), 2000 + j.Treasures.Sum(t => JungleBook.Points(j[t].Thing)));
    }

    [Fact]
    public void Every_deadly_pit_has_a_way_across_and_holes_have_no_logs()
    {
        foreach (var s in JungleBook.Jungle.Screens)
        {
            if (s.Deadly) Assert.True(s.Shifting || s.Vine || s.Crocs, $"screen {s.Index}");
            if (s.Crocs) Assert.Equal(Pit.Water, s.Pit);
            if (s.Pit == Pit.Holes) Assert.Equal(0, s.Logs);
        }
    }

    // ───────────────────────────── running and jumping ─────────────────────────────

    [Fact]
    public void Harry_runs_and_a_running_jump_goes_a_fixed_way()
    {
        var w = In(Little());
        Run(w, 60, new Controls(1));
        Assert.Equal(400, w.Harry.X, 1);
        Sim.Step(w, new Controls(1, 0, true));
        Assert.Equal(Pose.Air, w.Harry.Pose);
        double x0 = w.Harry.X;
        Run(w, Steps(Sim.AirTime) + 2, new Controls(-1));
        Assert.Equal(Pose.Ground, w.Harry.Pose);
        Assert.InRange(w.Harry.X - x0, Sim.JumpLength - 15, Sim.JumpLength + 5);
        Assert.Equal(Field.Path, w.Harry.Y);
    }

    [Fact]
    public void Off_the_edge_is_the_next_screen_and_in_the_tunnel_three_on()
    {
        var w = In(Little(), 1, 1000);
        Run(w, 10, new Controls(1));
        Assert.Equal(2, w.Screen);
        Assert.True(w.Harry.X < 100);
        var u = In(Little(), 1, 20, under: true);
        Run(w, 1);
        Run(u, 10, new Controls(-1));
        Assert.Equal(7, u.Screen);
        Assert.True(u.Harry.Under);
    }

    [Fact]
    public void A_hole_drops_Harry_into_the_tunnel_for_a_hundred_points()
    {
        var w = In(Little(s => s.Pit = Pit.Holes), 1, 400);
        Run(w, 120, new Controls(1));
        Assert.True(w.Harry.Under);
        Assert.Equal(Field.Floor, w.Harry.Y);
        Assert.Equal(1900, w.Score);
    }

    [Fact]
    public void Running_jumps_clear_three_holes_from_their_edges()
    {
        var w = In(Little(s => s.Pit = Pit.Holes), 1, 60);
        var p = new Pilot();
        for (int i = 0; i < 60 * 6 && w.Screen == 1; i++) Sim.Step(w, p.Decide(w));
        Assert.False(w.Harry.Under);
        Assert.Equal(2, w.Screen);
    }

    // ───────────────────────────── pits, crocodiles, the vine ─────────────────────────────

    [Fact]
    public void Tar_swallows_Harry()
    {
        var w = In(Little(s => { s.Pit = Pit.Tar; s.Vine = true; }), 1, 300);
        Run(w, 30, new Controls(1));
        Assert.Equal(Pose.Sinking, w.Harry.Pose);
        Run(w, 70);
        Assert.Equal(Pose.Dead, w.Harry.Pose);
        Assert.Equal("the tar pit", w.Cause);
    }

    [Fact]
    public void A_shifting_pit_opens_and_shuts_and_can_be_crossed_shut()
    {
        var j = Little(s => { s.Pit = Pit.Quicksand; s.Shifting = true; });
        var s = j[1];
        var halves = Enumerable.Range(0, 400).Select(i => Sim.PitHalfWidth(s, i * 0.02)).ToList();
        Assert.Contains(halves, h => h == 0);
        Assert.Contains(halves, h => h == JungleBook.PitHalf);
        // Wait until it's just shut, then walk straight across.
        double t = 0;
        while (Sim.PitHalfWidth(s, t) > 0 || Sim.PitHalfWidth(s, t - 0.02) == 0) t += 1.0 / 60;
        var w = In(j, 1, Field.Mid - 210);
        w.Clock = t;
        Run(w, Steps(1.5), new Controls(1));
        Assert.Equal(Pose.Ground, w.Harry.Pose);
        Assert.True(w.Harry.X > Field.Mid + JungleBook.PitHalf);
    }

    [Fact]
    public void A_crocodile_head_is_safe_with_its_jaws_shut_and_not_when_they_open()
    {
        var j = Little(s => { s.Pit = Pit.Water; s.Crocs = true; });
        var s = j[1];
        double t = 0;
        while (Sim.JawsOpenAt(s, t) || Sim.JawsOpenAt(s, t - 0.02) == false) t += 1.0 / 60;
        // Just shut: standing on the middle head.
        var w = In(j, 1, Sim.CrocX(1));
        w.Clock = t;
        Run(w, Steps(Sim.JawsShut - 0.2));
        Assert.Equal(Pose.Ground, w.Harry.Pose);
        Run(w, Steps(0.5));
        Assert.Equal(Pose.Dead, w.Harry.Pose);
        Assert.Equal("a crocodile", w.Cause);
    }

    [Fact]
    public void Harry_catches_the_vine_and_lets_go_beyond_the_pit()
    {
        var j = Little(s => { s.Pit = Pit.Tar; s.Vine = true; });
        var p = new Pilot();
        var w = In(j, 1, 200);
        bool swung = false;
        for (int i = 0; i < 60 * 20 && w.Screen == 1 && w.Harry.Pose != Pose.Dead; i++)
        {
            Sim.Step(w, p.Decide(w));
            swung |= w.Harry.Pose == Pose.Vine;
        }
        Assert.True(swung);
        Assert.Equal(2, w.Screen);
    }

    // ───────────────────────────── the tunnel ─────────────────────────────

    [Fact]
    public void Down_the_ladder_to_the_tunnel_and_up_again_hopping_off_to_one_side()
    {
        var w = In(Little(s => s.Pit = Pit.Ladder), 1, Field.Mid - 40);
        Sim.Step(w, new Controls(0, 1));
        Assert.Equal(Pose.Ladder, w.Harry.Pose);
        Run(w, 140, new Controls(0, 1));
        Assert.True(w.Harry.Under);
        Assert.Equal(Pose.Ground, w.Harry.Pose);
        Sim.Step(w, new Controls(0, -1));
        Run(w, 140, new Controls(0, -1));
        Assert.Equal(Field.Path, w.Harry.Y);
        Sim.Step(w, new Controls(1));
        Run(w, 40);
        Assert.Equal(Pose.Ground, w.Harry.Pose);
        Assert.False(w.Harry.Under);
        Assert.True(w.Harry.X > Field.Mid + JungleBook.HoleHalf);
    }

    [Fact]
    public void A_brick_wall_blocks_the_tunnel()
    {
        var w = In(Little(s => s.Wall = 1), 1, 600, under: true);
        Run(w, 120, new Controls(1));
        Assert.Equal(1, w.Screen);
        Assert.True(w.Harry.X < JungleBook.WallRight - JungleBook.WallHalf);
    }

    [Fact]
    public void The_scorpion_comes_for_Harry_and_can_be_jumped()
    {
        var w = In(Little(s => s.Scorpion = true), 1, 300, under: true);
        w.ScorpionX = 800;
        Run(w, 60);
        Assert.True(w.ScorpionX < 800);
        Run(w, 60 * 10);
        Assert.Equal(Pose.Dead, w.Harry.Pose);
        Assert.Equal("the scorpion", w.Cause);

        var v = In(Little(s => s.Scorpion = true), 1, 300, under: true);
        v.ScorpionX = 800;
        for (int i = 0; i < 60 * 3; i++) Sim.Step(v, new Controls(1, 0, v.Harry.Pose == Pose.Ground && v.ScorpionX - v.Harry.X is > 0 and < 80));
        Assert.NotEqual(Pose.Dead, v.Harry.Pose);
        Assert.True(v.Harry.X > v.ScorpionX || v.Screen != 1);
    }

    // ───────────────────────────── logs, dangers and treasure ─────────────────────────────

    [Fact]
    public void A_log_costs_a_hundred_points_once_per_bump_and_rolling_ones_roll_left()
    {
        var j = Little(s => s.Logs = 1);
        var w = In(j, 1, Sim.StillLogs[0] - 60);
        Run(w, 60, new Controls(1));
        Assert.Equal(1900, w.Score);
        var r = Little(s => { s.Logs = 1; s.Rolling = true; });
        double a = Sim.Logs(r[1], 0).First(), b = Sim.Logs(r[1], 0.5).First();
        Assert.Equal(Sim.LogSpeed * 0.5, a - b, 1);
    }

    [Fact]
    public void A_fire_kills_and_a_treasure_scores()
    {
        var w = In(Little(s => s.Thing = Thing.Fire), 1, JungleBook.ThingX - 100);
        Run(w, 40, new Controls(1));
        Assert.Equal(Pose.Dead, w.Harry.Pose);

        var t = In(Little(), 5, JungleBook.ThingX - 100);
        Run(t, 40, new Controls(1));
        Assert.True(t.Taken[0]);
        Assert.Equal(2000 + JungleBook.Points(Thing.GoldBar), t.Score);
        Assert.Contains(Sfx.Treasure, t.Heard);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Three_lives_back_where_he_came_in_then_the_game_is_over()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        w.Screen = 3;
        w.SafeScreen = 3;
        w.SafeSide = 1;
        for (int life = 0; life < GameEngine.Lives0; life++)
        {
            Sim.Die(g.World, "test");
            g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
            if (life < GameEngine.Lives0 - 1)
            {
                Assert.Equal(3, g.World.Screen);
                Assert.True(g.World.Harry.X > 900);
            }
        }
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Twenty_minutes_and_the_game_is_over()
    {
        var g = Engine();
        g.Go();
        g.World.Clock = Sim.GameSeconds - 0.5;
        g.Run(40);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(0, g.TimeLeft);
    }

    [Fact]
    public void Every_treasure_found_counts_the_time_left_into_the_score_then_initials()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        Array.Fill(w.Taken, true);
        w.Taken[0] = false;
        w.Screen = w.Jungle.Treasures[0];
        w.Harry.X = JungleBook.ThingX - 60;
        w.Clock = 600;
        g.Run(30, new Controls(1));
        Assert.Equal(Phase.Won, g.Phase);
        int score = g.Score;
        g.Run(60 * 20);
        Assert.InRange(g.Score - score, (Sim.GameSeconds - 600) * GameEngine.SecondPoints - 20, (Sim.GameSeconds - 600) * GameEngine.SecondPoints + 20);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run(60 * 30);
        Assert.True(g.World.Screen != 0 || g.World.Harry.Under);
        g.Run((int)(60 * (GameEngine.AttractSeconds - 30)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_the_clock()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        double t = g.World.Clock;
        g.Pause();
        g.Run(200);
        Assert.Equal(t, g.World.Clock);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Clock > t);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = In(Little());
        var c = w.Copy();
        Run(c, 60, new Controls(1));
        Assert.Equal(100, w.Harry.X);
        Assert.NotEqual(w.Harry.X, c.Harry.X);
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Jump);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go();
        double x = g.World.Harry.X;
        g.Run(30, new Controls(1));
        Assert.True(g.World.Harry.X > x + 100);
    }

    // ───────────────────────────── the computer Harry ─────────────────────────────

    [Fact]
    public void The_computer_finds_every_treasure_in_time_and_just_running_and_jumping_finds_none()
    {
        var g = Engine();
        g.Go();
        for (int t = 0; t < 60 * 60 * 20 && g.State == GameState.Playing && g.Phase != Phase.Won; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(Phase.Won, g.Phase);
        Assert.Equal(GameEngine.Lives0, g.Lives);
        Assert.True(g.World.Clock < 12 * 60);

        var lazy = Engine();
        lazy.Go();
        for (int t = 0; t < 60 * 60 * 20 && lazy.State == GameState.Playing; t++) lazy.Tick(new Controls(1, 0, t % 30 == 0));
        Assert.NotEqual(GameState.Playing, lazy.State);
        Assert.Equal(0, lazy.World.Taken.Count(x => x));
    }
}
