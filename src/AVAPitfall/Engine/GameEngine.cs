namespace AVAPitfall.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A moment before the game starts.</summary>
    Start,
    Play,
    /// <summary>Lost: a moment, then back at the end of the screen he came in by, or the game is over.</summary>
    Dying,
    /// <summary>Every treasure found.</summary>
    Won,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Treasure, Log, Fall, Sink, Die, Screen, Tick,
    Yell,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.5, DyingSeconds = 2, WonSeconds = 6, OverSeconds = 4;
    public const int Lives0 = 3, SecondPoints = 10;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music;
    private int _minute = -1;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    public double TimeLeft => Math.Max(0, Sim.GameSeconds - World.Clock);
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Jump && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, World.Taken.Count(t => t));
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Sim.Start(World, JungleBook.Jungle);
        Lives = Lives0;
        _minute = -1;
        SetPhase(Phase.Start);
        Message = "FIND THE TREASURE";
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                Sim.Step(w, c);
                int minute = (int)(TimeLeft / 60);
                if (_minute >= 0 && minute != _minute && minute < 5) Play(Sfx.Tick);
                _minute = minute;
                if (w.AllTaken)
                {
                    SetPhase(Phase.Won);
                    Music(false);
                    Play(Sfx.Victory);
                    Message = "EVERY TREASURE FOUND!";
                    MessageTime = WonSeconds;
                }
                else if (TimeLeft <= 0)
                {
                    Message = "TIME'S UP";
                    MessageTime = 3;
                    End();
                    return;
                }
                else if (w.Harry.Pose == Pose.Dead) { SetPhase(Phase.Dying); Music(false); }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Respawn(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Won:
                // The time to spare, counted into the score.
                if (w.Clock < Sim.GameSeconds && PhaseTime > 0.6)
                {
                    double take = Math.Min(Sim.GameSeconds - w.Clock, dt * 120);
                    int before = (int)w.Clock;
                    w.Clock += take;
                    w.Score += ((int)w.Clock - before) * SecondPoints;
                    if (before / 10 != (int)w.Clock / 10) Play(Sfx.Tick);
                }
                if (PhaseTime > WonSeconds && w.Clock >= Sim.GameSeconds - 0.001)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    State = GameState.GameOver;
                    StateTime = 0;
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into the jungle, past the opening moment.</summary>
    public void Go()
    {
        NewGame();
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
