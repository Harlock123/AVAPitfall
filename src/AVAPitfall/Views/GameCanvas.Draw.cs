using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPitfall.Engine;

namespace AVAPitfall.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPitfall/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double PathY = Field.Path, Floor = Field.Floor, Roof = Field.Roof, Mid = Field.Mid;
    private const uint Ochre = 0xFFC89A3A, Earth = 0xFF7A5420, Tunnel = 0xFF1A1206, Leaf = 0xFF1E6A1E, LeafDark = 0xFF0E4A12, Trunk = 0xFF5A3A16;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawJungle(dc, JungleBook.Jungle[3], 0, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawJungle(dc, JungleBook.Jungle[5], 0, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                var w = g.World;
                DrawJungle(dc, w.Def, w.Clock, true, w);
                DrawHud(dc, w);
                if (g.Message is { } msg && g.Phase is Phase.Start or Phase.Won)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, 230, Field.Width, 72));
                    Text(dc, msg, new Point(512, 266), 54, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 715), 24, Yellow, center: true);
    }

    // ───────────────────────────── the jungle ─────────────────────────────

    /// <summary>One screen of jungle: trees, the path and what's on it, the ground, the tunnel and what's in it.</summary>
    private void DrawJungle(DrawingContext dc, ScreenDef s, double clock, bool things, World? w = null)
    {
        int seed = s.Index;
        // Behind the trees, a green gloom.
        dc.FillRectangle(Brush(0xFF0C2A10), new Rect(0, 60, Field.Width, PathY - 60));
        for (int i = 0; i < 6; i++)
        {
            double x = 60 + i * 180 + Hash(i, seed, 1) % 70;
            dc.FillRectangle(Brush(0xFF103814), new Rect(x - 20, 60, 40, PathY - 60));
        }
        // The trunks, with roots spreading at the foot.
        for (int i = 0; i < 4; i++)
        {
            double x = 110 + i * 260 + Hash(i, seed, 2) % 60, wd = 26 + Hash(i, seed, 3) % 14;
            dc.FillRectangle(Brush(Trunk), new Rect(x - wd / 2, 150, wd, PathY - 150));
            dc.FillRectangle(Brush(0xFF6A4A20), new Rect(x - wd / 2 + 4, 150, 5, PathY - 150));
            Poly(dc, Trunk, null, 0, new Point(x - wd / 2 - 14, PathY), new Point(x - wd / 2, PathY - 30), new Point(x + wd / 2, PathY - 30), new Point(x + wd / 2 + 14, PathY));
        }
        // The canopy: layered leaves.
        dc.FillRectangle(Brush(LeafDark), new Rect(0, 60, Field.Width, 70));
        for (int i = 0; i < 26; i++)
        {
            double x = i * 42 + Hash(i, seed, 4) % 20, y = 120 + Hash(i, seed, 5) % 50;
            dc.DrawEllipse(Brush(i % 2 == 0 ? Leaf : 0xFF2A7A22), null, new Point(x, y), 34, 22);
        }
        // Hanging creepers (just for show).
        for (int i = 0; i < 5; i++)
        {
            double x = 50 + Hash(i, seed, 6) % 920, len = 60 + Hash(i, seed, 7) % 90;
            dc.DrawLine(Pen(0xFF2A6A1A, 2), new Point(x, 140), new Point(x + Math.Sin(clock + i) * 3, 140 + len));
        }
        // The ground.
        dc.FillRectangle(Brush(Ochre), new Rect(0, PathY, Field.Width, Roof - PathY));
        dc.FillRectangle(Brush(0xFFE0B850), new Rect(0, PathY, Field.Width, 4));
        dc.FillRectangle(Brush(Tunnel), new Rect(0, Roof, Field.Width, Floor - Roof));
        dc.FillRectangle(Brush(Earth), new Rect(0, Floor, Field.Width, Field.Height - Floor));
        dc.FillRectangle(Brush(0xFF9A7030), new Rect(0, Floor, Field.Width, 4));
        // The pit or holes.
        DrawPit(dc, s, clock);
        // The tunnel: a ladder, a wall, the scorpion.
        if (s.Pit == Pit.Ladder) DrawLadder(dc);
        if (s.Wall != 0) DrawWall(dc, s.Wall < 0 ? JungleBook.WallLeft : JungleBook.WallRight);
        if (s.Vine) DrawVine(dc, clock);
        if (!things) return;
        if (s.Scorpion && w != null) DrawScorpion(dc, w.ScorpionX, w.Harry.X > w.ScorpionX ? 1 : -1, clock);
        foreach (double lx in Sim.Logs(s, clock)) DrawLog(dc, lx, s.Rolling ? clock : 0);
        if (s.Thing is Thing.Fire or Thing.Cobra) DrawDanger(dc, s.Thing, clock);
        else if (s.Treasure >= 0 && w != null && !w.Taken[s.Treasure]) DrawTreasure(dc, s.Thing, JungleBook.ThingX, PathY, clock);
        if (w != null) DrawHarry(dc, w.Harry, clock);
    }

    private void DrawPit(DrawingContext dc, ScreenDef s, double clock)
    {
        foreach (double hx in Sim.Holes(s))
        {
            dc.FillRectangle(Brush(0xFF000000), new Rect(hx - JungleBook.HoleHalf, PathY, JungleBook.HoleHalf * 2, Roof - PathY));
            dc.FillRectangle(Brush(0xFF3A2A10), new Rect(hx - JungleBook.HoleHalf, PathY, JungleBook.HoleHalf * 2, 4));
        }
        if (!s.Deadly) return;
        double half = Sim.PitHalfWidth(s, clock);
        if (half < 1) return;
        uint fill = s.Pit switch { Pit.Tar => 0xFF101010, Pit.Quicksand => 0xFF8A5A1A, _ => 0xFF2060C0 };
        // A pit with a rounded bottom, set into the ground.
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(Mid - half, PathY + 2), true);
            c.LineTo(new Point(Mid + half, PathY + 2));
            c.CubicBezierTo(new Point(Mid + half, PathY + 40), new Point(Mid + half * 0.6, PathY + 56), new Point(Mid, PathY + 56));
            c.CubicBezierTo(new Point(Mid - half * 0.6, PathY + 56), new Point(Mid - half, PathY + 40), new Point(Mid - half, PathY + 2));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(fill), null, g);
        // The surface: ripples on water, bubbles in tar, swirls in quicksand.
        for (double x = Mid - half + 10; x < Mid + half - 10; x += 26)
        {
            double y = PathY + 8 + Math.Sin(clock * 3 + x * 0.05) * 2;
            uint shine = s.Pit switch { Pit.Tar => 0xFF404040, Pit.Quicksand => 0xFFB07A30, _ => 0xFF70B0F0 };
            dc.DrawLine(Pen(shine, 2), new Point(x, y), new Point(x + 12, y));
        }
        if (s.Crocs)
        {
            bool open = Sim.JawsOpenAt(s, clock);
            for (int k = 0; k < 3; k++) DrawCroc(dc, Sim.CrocX(k), open);
        }
    }

    private static void DrawCroc(DrawingContext dc, double x, bool open)
    {
        const uint green = 0xFF2A8A2A, dark = 0xFF145214;
        double y = PathY + 4;
        // Head facing left: the back of the head, the snout, the eye.
        dc.DrawEllipse(Brush(green), Pen(dark, 2), new Point(x + 14, y), 24, 10);
        if (open)
        {
            Poly(dc, green, dark, 2, new Point(x - 4, y - 2), new Point(x - 36, y - 26), new Point(x - 30, y - 30), new Point(x + 4, y - 8));
            Poly(dc, 0xFFE04040, null, 0, new Point(x - 4, y - 4), new Point(x - 32, y - 24), new Point(x - 34, y + 2));
            for (int k = 0; k < 4; k++) Poly(dc, 0xFFFFFFFF, null, 0, new Point(x - 30 + k * 7, y - 22 + k * 4), new Point(x - 27 + k * 7, y - 17 + k * 4), new Point(x - 25 + k * 7, y - 21 + k * 4));
            dc.DrawEllipse(Brush(green), Pen(dark, 2), new Point(x - 18, y + 1), 18, 5);
        }
        else dc.DrawEllipse(Brush(green), Pen(dark, 2), new Point(x - 16, y), 22, 7);
        dc.FillEllipse(x + 20, y - 8, 4, 0xFFF0F040);
        dc.FillEllipse(x + 21, y - 8, 1.6, 0xFF000000);
    }

    private static void DrawLadder(DrawingContext dc)
    {
        const uint wood = 0xFF8A6A30;
        dc.FillRectangle(Brush(0xFF000000), new Rect(Mid - 30, PathY, 60, Roof - PathY));
        dc.DrawLine(Pen(wood, 4), new Point(Mid - 16, PathY + 4), new Point(Mid - 16, Floor));
        dc.DrawLine(Pen(wood, 4), new Point(Mid + 16, PathY + 4), new Point(Mid + 16, Floor));
        for (double y = PathY + 16; y < Floor; y += 22) dc.DrawLine(Pen(wood, 3), new Point(Mid - 16, y), new Point(Mid + 16, y));
    }

    private static void DrawWall(DrawingContext dc, double x)
    {
        double half = JungleBook.WallHalf;
        dc.FillRectangle(Brush(0xFF8A2A1A), new Rect(x - half, Roof, half * 2, Floor - Roof));
        for (int r = 0; r < (Floor - Roof) / 18; r++)
        {
            double y = Roof + r * 18;
            dc.DrawLine(Pen(0xFF4A1A0A, 2), new Point(x - half, y), new Point(x + half, y));
            double off = r % 2 == 0 ? 0 : 15;
            for (double bx = x - half + off; bx < x + half; bx += 30) dc.DrawLine(Pen(0xFF4A1A0A, 2), new Point(bx, y), new Point(bx, y + 18));
        }
    }

    private static void DrawVine(DrawingContext dc, double clock)
    {
        var (tx, ty, _) = Sim.Vine(clock);
        // A slight curve, as a vine would hang.
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(Sim.VineX, Sim.VineY), false);
            c.QuadraticBezierTo(new Point((Sim.VineX + tx) / 2 + (ty - Sim.VineY) * 0.04, (Sim.VineY + ty) / 2), new Point(tx, ty));
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(0xFF6A9A2A, 4), g);
        dc.DrawGeometry(null, Pen(0xFF3A6A12, 1.5), g);
        dc.FillEllipse(tx, ty, 4, 0xFF4A7A1A);
    }

    private static void DrawLog(DrawingContext dc, double x, double roll)
    {
        double y = PathY - 18, r = 18;
        dc.FillRectangle(Brush(0xFF6A4018), new Rect(x - r - 2, y - r + 2, r * 2 + 4, r * 2 - 2), 10);
        dc.DrawEllipse(Brush(0xFFB08850), Pen(0xFF5A3410, 2), new Point(x, y + 1), r - 4, r - 2);
        dc.DrawEllipse(null, Pen(0xFF8A6030, 1.5), new Point(x, y + 1), r - 9, r - 8);
        double a = -roll * 14;
        dc.DrawLine(Pen(0xFF7A5020, 2), new Point(x, y + 1), new Point(x + Math.Cos(a) * (r - 5), y + 1 + Math.Sin(a) * (r - 4)));
    }

    private void DrawDanger(DrawingContext dc, Thing t, double clock)
    {
        double x = JungleBook.ThingX, y = PathY;
        if (t == Thing.Fire)
        {
            dc.FillRectangle(Brush(0xFF5A3010), new Rect(x - 18, y - 8, 36, 8), 3);
            for (int k = 0; k < 3; k++)
            {
                double fx = x - 10 + k * 10, h = 30 + Math.Sin(clock * 14 + k * 2) * 8;
                Poly(dc, 0xFFFF5010, null, 0, new Point(fx - 8, y - 6), new Point(fx + Math.Sin(clock * 9 + k) * 3, y - 6 - h), new Point(fx + 8, y - 6));
                Poly(dc, 0xFFFFE040, null, 0, new Point(fx - 4, y - 6), new Point(fx, y - 6 - h * 0.55), new Point(fx + 4, y - 6));
            }
            return;
        }
        // The cobra: coiled, its hood spread, swaying.
        double sway = Math.Sin(clock * 3) * 3;
        dc.DrawEllipse(Brush(0xFF3A3A3A), Pen(0xFF101010, 2), new Point(x, y - 6), 18, 6);
        dc.DrawEllipse(Brush(0xFF4A4A4A), Pen(0xFF101010, 2), new Point(x, y - 12), 13, 5);
        dc.DrawLine(Pen(0xFF4A4A4A, 7), new Point(x, y - 14), new Point(x + sway, y - 40));
        dc.DrawEllipse(Brush(0xFF5A5A5A), Pen(0xFF101010, 2), new Point(x + sway, y - 44), 10, 12);
        dc.FillEllipse(x + sway - 4, y - 48, 1.8, 0xFFFF2020);
        dc.FillEllipse(x + sway + 4, y - 48, 1.8, 0xFFFF2020);
        if ((int)(clock * 4) % 2 == 0) dc.DrawLine(Pen(0xFFE02020, 1.5), new Point(x + sway, y - 36), new Point(x + sway - 3, y - 28));
    }

    private void DrawTreasure(DrawingContext dc, Thing t, double x, double y, double clock)
    {
        double glint = 0.5 + 0.5 * Math.Sin(clock * 5);
        switch (t)
        {
            case Thing.MoneyBag:
                dc.DrawEllipse(Brush(0xFFB0B0A0), Pen(0xFF606050, 2), new Point(x, y - 16), 16, 15);
                Poly(dc, 0xFFB0B0A0, 0xFF606050, 2, new Point(x - 7, y - 30), new Point(x - 10, y - 40), new Point(x + 10, y - 40), new Point(x + 7, y - 30));
                Text(dc, "$", new Point(x, y - 16), 24, 0xFF2A6A2A, center: true, shadow: false);
                break;
            case Thing.SilverBar or Thing.GoldBar:
            {
                uint c = t == Thing.GoldBar ? 0xFFF0C030 : 0xFFD0D8E0, e = t == Thing.GoldBar ? 0xFF8A6010 : 0xFF707880;
                Poly(dc, c, e, 2, new Point(x - 22, y), new Point(x + 22, y), new Point(x + 16, y - 12), new Point(x - 16, y - 12));
                Poly(dc, c, e, 2, new Point(x - 14, y - 12), new Point(x + 14, y - 12), new Point(x + 9, y - 24), new Point(x - 9, y - 24));
                dc.FillEllipse(x + 6, y - 20, 2 + glint * 2, 0xFFFFFFFF);
                break;
            }
            case Thing.Ring:
                dc.DrawEllipse(null, Pen(0xFFF0C030, 5), new Point(x, y - 14), 13, 13);
                Poly(dc, 0xFFE0F8FF, 0xFF80A0C0, 1.5, new Point(x - 8, y - 30), new Point(x + 8, y - 30), new Point(x, y - 42));
                dc.FillEllipse(x + 3, y - 34, 1.5 + glint * 2.5, 0xFFFFFFFF);
                break;
        }
    }

    private static void DrawScorpion(DrawingContext dc, double x, int dir, double clock)
    {
        double y = Floor;
        const uint body = 0xFFE0E0E0, dark = 0xFF707070;
        dc.DrawEllipse(Brush(body), Pen(dark, 1.5), new Point(x, y - 7), 14, 6);
        for (int k = 0; k < 3; k++)
        {
            double lx = x - 8 + k * 8, step = Math.Sin(clock * 16 + k) * 2;
            dc.DrawLine(Pen(dark, 1.5), new Point(lx, y - 5), new Point(lx - 4 + step, y));
        }
        // The claws ahead, the tail curled over behind.
        dc.DrawLine(Pen(body, 3), new Point(x + dir * 12, y - 8), new Point(x + dir * 22, y - 12));
        dc.DrawEllipse(Brush(body), null, new Point(x + dir * 24, y - 12), 4, 3);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x - dir * 12, y - 8), false);
            c.CubicBezierTo(new Point(x - dir * 26, y - 16), new Point(x - dir * 22, y - 34), new Point(x - dir * 8, y - 30));
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(body, 4), g);
        dc.FillEllipse(x - dir * 6, y - 28, 3, 0xFFE04040);
    }

    /// <summary>Harry: a pith helmet, a green shirt, khaki trousers, feet on (x, y).</summary>
    private void DrawHarry(DrawingContext dc, Harry h, double clock)
    {
        double x = h.X, y = h.Y;
        int f = h.Facing;
        const uint skin = 0xFFF0C090, shirt = 0xFF2A9A3A, trousers = 0xFFB89A60, boots = 0xFF4A2A10, helmet = 0xFFE8D8A8;
        if (h.Pose == Pose.Dead && Engine.PhaseTime > 0)
        {
            // Lying flat.
            dc.FillRectangle(Brush(trousers), new Rect(x - 30, y - 10, 28, 10), 3);
            dc.FillRectangle(Brush(shirt), new Rect(x - 4, y - 12, 24, 12), 3);
            dc.FillEllipse(x + 28, y - 7, 7, skin);
            dc.DrawEllipse(Brush(helmet), null, new Point(x + 40, y - 6), 7, 9);
            return;
        }
        using var clip = h.Pose == Pose.Sinking ? dc.PushClip(new Rect(x - 40, 0, 80, PathY + 2)) : (DrawingContext.PushedState?)null;
        double run = Math.Sin(h.Walk * 2.2);
        bool air = h.Pose == Pose.Air, climb = h.Pose == Pose.Ladder, hang = h.Pose == Pose.Vine;
        // Legs.
        double hip = y - 30;
        if (air)
        {
            dc.DrawLine(Pen(trousers, 7), new Point(x - 3, hip), new Point(x - 10 * f, hip + 14));
            dc.DrawLine(Pen(trousers, 7), new Point(x + 3, hip), new Point(x + 12 * f, hip + 10));
            dc.DrawLine(Pen(boots, 7), new Point(x - 10 * f, hip + 14), new Point(x - 8 * f, hip + 24));
            dc.DrawLine(Pen(boots, 7), new Point(x + 12 * f, hip + 10), new Point(x + 18 * f, hip + 16));
        }
        else if (hang || climb)
        {
            double k = climb ? run * 6 : 2;
            dc.DrawLine(Pen(trousers, 7), new Point(x - 4, hip), new Point(x - 5, hip + 22 + k));
            dc.DrawLine(Pen(trousers, 7), new Point(x + 4, hip), new Point(x + 5, hip + 22 - k));
            dc.FillRectangle(Brush(boots), new Rect(x - 9, hip + 22 + k, 8, 7));
            dc.FillRectangle(Brush(boots), new Rect(x + 1, hip + 22 - k, 8, 7));
        }
        else
        {
            double a = h.Vx == 0 ? 0 : run;
            dc.DrawLine(Pen(trousers, 7), new Point(x, hip), new Point(x + a * 10, y - 6));
            dc.DrawLine(Pen(trousers, 7), new Point(x, hip), new Point(x - a * 10, y - 6));
            dc.FillRectangle(Brush(boots), new Rect(x + a * 10 - 4 + f * 2, y - 7, 10, 7), 2);
            dc.FillRectangle(Brush(boots), new Rect(x - a * 10 - 4 + f * 2, y - 7, 10, 7), 2);
        }
        // Body and arms.
        dc.FillRectangle(Brush(shirt), new Rect(x - 10, y - 54, 20, 26), 4);
        dc.FillRectangle(Brush(0xFF6A4A20), new Rect(x - 10, y - 32, 20, 4));
        if (hang || climb)
        {
            double k = climb ? run * 6 : 0;
            dc.DrawLine(Pen(shirt, 6), new Point(x - 7, y - 50), new Point(x - 8, y - 70 + k));
            dc.DrawLine(Pen(shirt, 6), new Point(x + 7, y - 50), new Point(x + 8, y - 70 - k));
            dc.FillEllipse(x - 8, y - 72 + k, 4, skin);
            dc.FillEllipse(x + 8, y - 72 - k, 4, skin);
        }
        else
        {
            double a = air ? -0.9 : h.Vx == 0 ? 0.15 : -run * 0.7;
            dc.DrawLine(Pen(shirt, 6), new Point(x, y - 50), new Point(x + Math.Sin(a) * 16 * f, y - 50 + Math.Cos(a) * 16));
            dc.FillEllipse(x + Math.Sin(a) * 18 * f, y - 50 + Math.Cos(a) * 18, 3.5, skin);
        }
        // Head and helmet.
        dc.FillEllipse(x + f * 2, y - 61, 8, skin);
        dc.DrawEllipse(Brush(helmet), Pen(0xFF9A8A60, 1.5), new Point(x + f, y - 67), 11, 6);
        dc.FillRectangle(Brush(helmet), new Rect(x - 14 + f, y - 66, 28, 3), 1.5);
        dc.FillEllipse(x + f * 5, y - 61, 1.6, 0xFF000000);
    }

    // ───────────────────────────── the top line and the map ─────────────────────────────

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, 60));
        Text(dc, $"{g.Score}", new Point(24, 30), 44, White);
        double left = g.TimeLeft;
        string clock = $"{(int)left / 60}:{(int)left % 60:00}";
        Text(dc, clock, new Point(512, 30), 44, left < 120 && (int)(_clock * 3) % 2 == 0 ? Red : White, center: true);
        int found = w.Taken.Count(t => t);
        Text(dc, $"TREASURES {found}/{w.Taken.Length}", new Point(780, 30), 28, Yellow, center: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
        {
            double x = 1000 - i * 22;
            dc.DrawLine(Pen(0xFF2A9A3A, 8), new Point(x, 18), new Point(x, 44));
            dc.DrawEllipse(Brush(0xFFE8D8A8), null, new Point(x, 14), 8, 4);
        }
        DrawMap(dc, w);
    }

    /// <summary>The ring of the jungle as a strip: treasures still to find, Harry, and the screens either side.</summary>
    private void DrawMap(DrawingContext dc, World w)
    {
        var j = w.Jungle;
        double x0 = 32, y = 742, cw = (Field.Width - 64) / j.Count;
        dc.FillRectangle(Brush(0x90000000), new Rect(x0 - 8, y - 14, Field.Width - 48, 28), 6);
        for (int i = 0; i < j.Count; i++)
        {
            var s = j[i];
            uint c = s.Treasure >= 0 ? (w.Taken[s.Treasure] ? 0xFF4A4A30 : 0xFFF0C030) : s.Pit == Pit.Ladder ? 0xFF8A6A30 : 0xFF2A5A2A;
            dc.FillRectangle(Brush(c), new Rect(x0 + i * cw, y - (s.Treasure >= 0 ? 7 : 3), Math.Max(1, cw - 1.5), s.Treasure >= 0 ? 14 : 6));
        }
        double hx = x0 + (w.Screen + 0.5) * cw;
        if ((int)(_clock * 4) % 2 == 0 || Engine.State == GameState.Attract)
            Poly(dc, 0xFFFFFFFF, null, 0, new Point(hx - 6, y - 16), new Point(hx + 6, y - 16), new Point(hx, y - 6));
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        var demo = new ScreenDef { Index = 11, Pit = Pit.Water, Crocs = true, Vine = true };
        DrawJungle(dc, demo, _clock, false);
        // Harry on the vine.
        var (vx, vy, _) = Sim.Vine(_clock);
        DrawHarry(dc, new Harry { X = vx, Y = vy + Harry.Height - 6, Pose = Pose.Vine, Facing = 1 }, _clock);
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, 60));
        string title = "AVA PITFALL!";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 108 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, i < 3 ? 0xFF40E040 : Yellow, center: true);
        }
        Text(dc, "AFTER DAVID CRANE'S 1982 GAME", new Point(cx, 176), 26, White, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 410, 200, 820, 128), 10);
        Text(dc, "FIND ALL 32 TREASURES IN THE JUNGLE IN TWENTY MINUTES.", new Point(cx, 224), 26, White, center: true);
        Text(dc, "JUMP THE LOGS, THE FIRES AND THE COBRAS; SWING OVER THE PITS;", new Point(cx, 252), 26, White, center: true);
        Text(dc, "HOP THE CROCODILES WHILE THEIR JAWS ARE SHUT. THE TUNNEL BELOW", new Point(cx, 280), 26, White, center: true);
        Text(dc, "GOES THREE SCREENS AT A TIME, IF THE WALLS LET YOU.", new Point(cx, 308), 26, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 556), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT: RUN   SPACE: JUMP   UP/DOWN: CLIMB   DOWN: LET GO OF A VINE   P: PAUSE   M: SOUND", new Point(cx, 718), 20, 0xFFE0E0C0, center: true);
        Text(dc, "MUSIC: A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawJungle(dc, JungleBook.Jungle[7], _clock, false);
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, 60));
        bool all = g.World.AllTaken;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 360, 150, 720, 230), 12);
        Text(dc, all ? "EVERY TREASURE FOUND!" : "GAME OVER", new Point(cx, 220), all ? 70 : 100, (int)(_clock * 4) % 2 == 0 ? (all ? Yellow : Red) : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 300), 46, White, center: true);
        Text(dc, $"{g.World.Taken.Count(t => t)} OF {g.World.Taken.Length} TREASURES", new Point(cx, 350), 30, Yellow, center: true);
        Thing[] kinds = [Thing.MoneyBag, Thing.SilverBar, Thing.GoldBar, Thing.Ring];
        for (int k = 0; k < 4; k++) DrawTreasure(dc, kinds[k], cx - 180 + k * 120, PathY, _clock + k);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE GREATEST EXPLORERS", new Point(cx, 120), 56, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  TREASURES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
