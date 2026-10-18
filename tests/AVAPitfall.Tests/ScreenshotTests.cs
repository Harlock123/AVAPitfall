using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPitfall.Engine;
using AVAPitfall.Views;
using Controls = AVAPitfall.Engine.Controls;

namespace AVAPitfall.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PITFALL_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapitfall-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PITFALL_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPitfall-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer Harry play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 64200, 21);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Swinging() => Moment("vine", g => g.World.Harry.Pose == Pose.Vine && Math.Abs(g.World.Harry.X - Field.Mid) < 60);

    [AvaloniaFact]
    public void Crocodiles() => Moment("crocodiles", g => g.World.Def.Crocs && g.World.Harry.Pose == Pose.Air && Math.Abs(g.World.Harry.X - Field.Mid) < 160);

    [AvaloniaFact]
    public void The_tunnel()
    {
        var (w, c, g) = Open();
        g.Go();
        var j = g.World.Jungle;
        var s = j.Screens.First(s => s.Scorpion && s.Index > 4 && s.Pit is Pit.Tar or Pit.Water);
        g.World.Screen = s.Index;
        g.World.Harry = new Harry { X = 60, Y = Field.Floor, Under = true };
        g.World.ScorpionX = 800;
        // Run at the scorpion and jump it.
        for (int i = 0; i < 60 * 10 && !(g.World.Harry.Pose == Pose.Air && Math.Abs(g.World.Harry.X - g.World.ScorpionX) < 30); i++)
        {
            var h = g.World.Harry;
            g.Tick(new Controls(1, 0, h.Pose == Pose.Ground && g.World.ScorpionX - h.X < 80));
            if (i % 3 == 0) c.Advance(0.05);
        }
        Assert.Equal(Pose.Air, g.World.Harry.Pose);
        Shot(w, c, "tunnel");
    }

    [AvaloniaFact]
    public void A_ladder() => Moment("ladder", g => g.World.Harry.Pose == Pose.Ladder && g.World.Harry.Y > Field.Path + 90);

    [AvaloniaFact]
    public void Treasure() => Moment("treasure", g => g.World.Def.Thing == Thing.GoldBar && g.World.Def.Treasure >= 0 && !g.World.Taken[g.World.Def.Treasure]
                                                       && Math.Abs(g.World.Harry.X - JungleBook.ThingX) < 140 && !g.World.Harry.Under);

    [AvaloniaFact]
    public void Logs() => Moment("logs", g => g.World.Def.Rolling && g.World.Harry.Pose == Pose.Air && !g.World.Harry.Under
                                               && Sim.Logs(g.World.Def, g.World.Clock).Any(x => Math.Abs(x - g.World.Harry.X) < 40));

    [AvaloniaFact]
    public void Into_the_tar()
    {
        var (w, c, g) = Open();
        g.Go();
        var j = g.World.Jungle;
        var tar = j.Screens.First(s => s.Pit == Pit.Tar && !s.Shifting);
        g.World.Screen = tar.Index;
        g.World.Harry.X = 200;
        for (int i = 0; i < 60 * 3 && g.World.Harry.Pose != Pose.Dead; i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); if (g.World.Harry.Pose == Pose.Sinking && g.World.Harry.SinkTime > 0.4) break; }
        Assert.Equal(Pose.Sinking, g.World.Harry.Pose);
        Shot(w, c, "tar");
    }

    [AvaloniaFact]
    public void Every_treasure_found()
    {
        var (w, c, g) = Open();
        g.Go();
        Array.Fill(g.World.Taken, true);
        g.World.Taken[0] = false;
        g.World.Score = 109200;
        g.World.Clock = 14 * 60 + 12;
        var s = g.World.Jungle[g.World.Jungle.Treasures[0]];
        g.World.Screen = s.Index;
        g.World.Harry.X = JungleBook.ThingX - 60;
        for (int i = 0; i < 60 * 20 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "all-found");
    }
}
