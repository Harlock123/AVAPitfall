using AVAPitfall.Engine;

namespace AVAPitfall.Tests;

/// <summary>How the computer Harry gets on (set PITFALL_PROBE to run it, for tuning; PITFALL_PROBE=lazy just runs and jumps).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_Harry_does()
    {
        if (Environment.GetEnvironmentVariable("PITFALL_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var j = JungleBook.Jungle;
        output.WriteLine($"jungle: {j.Count} screens, {j.Treasures.Count} treasures; pits {string.Join(", ", j.Screens.GroupBy(s => s.Pit).Select(g => $"{g.Key} {g.Count()}"))}; " +
                         $"vines {j.Screens.Count(s => s.Vine)}, crocs {j.Screens.Count(s => s.Crocs)}, shifting {j.Screens.Count(s => s.Shifting)}, walls {j.Screens.Count(s => s.Wall != 0)}");
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "pitfall-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go();
        g.SetLives(mode == "lazy" ? 3 : 30);
        var deaths = new Dictionary<string, int>();
        var doing = new Dictionary<string, int>();
        var last = g.Phase;
        int t = 0, logs = 0;
        for (; t < 60 * 60 * 21 && g.State == GameState.Playing; t++)
        {
            var c = mode == "lazy" ? new Controls(1, 0, t % 30 == 0) : g.Pilot.Decide(g);
            int before = g.Score;
            g.Tick(c);
            if (g.World.Heard.Contains(Sfx.Log)) logs++;
            if (g.Phase == Phase.Dying && last != Phase.Dying) { var k = $"{g.World.Cause} on {g.World.Screen}{(g.World.Harry.Under ? " (under)" : "")}"; deaths[k] = deaths.GetValueOrDefault(k) + 1; }
            last = g.Phase;
            doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
        }
        var w = g.World;
        output.WriteLine($"{(w.AllTaken ? "ALL FOUND" : "not all")} after {t / 60}s (clock {w.Clock:0}); treasures {w.Taken.Count(x => x)}/{w.Taken.Length}; score {g.Score}; lives {g.Lives}; {deaths.Values.Sum()} deaths");
        foreach (var d in deaths.OrderByDescending(d => d.Value)) output.WriteLine($"   {d.Value} x {d.Key}");
        output.WriteLine("doing: " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(10).Select(d => $"{d.Key} {d.Value / 60}s")));
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
