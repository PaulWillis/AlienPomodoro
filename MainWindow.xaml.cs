using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AlienPomodoro;

public partial class MainWindow : Window
{
    private static readonly TimeSpan WorkDuration = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(5);
    private const int ProgressCells = 40;
    private const int MaxLogLines = 4;

    private enum Phase { Idle, Work, Break }

    private Phase _phase = Phase.Idle;
    private DateTime _phaseEnd;
    private TimeSpan _phaseLength;
    private TimeSpan _pausedRemaining;
    private bool _paused;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _typer = new() { Interval = TimeSpan.FromMilliseconds(18) };
    private readonly Queue<string> _pendingLines = new();
    private readonly List<string> _logLines = new();
    private string _typingLine = "";
    private int _typingPos;
    private bool _cursorOn;
    private int _cursorTicks;

    private readonly TallyStore _store = TallyStore.Load();

    private readonly Brush _phosphor;
    private readonly Brush _amber;

    public MainWindow()
    {
        InitializeComponent();
        _phosphor = (Brush)FindResource("Phosphor");
        _amber = (Brush)FindResource("Amber");

        _tick.Tick += (_, _) => OnTick();
        _typer.Tick += (_, _) => OnType();
        _tick.Start();
        _typer.Start();

        RenderTally();
        RenderIdle();

        Log("MU-TH-UR INTERFACE ONLINE.");
        Log($"CREW PRODUCTIVITY MONITOR ACTIVE. {_store.Count} CYCLE(S) LOGGED FOR {DateTime.Today:yyyy.MM.dd}.");
        Log("AWAITING INSTRUCTION. PRESS [ INITIATE ] TO BEGIN WORK CYCLE.");
    }

    // ---------------------------------------------------------------- timer

    private void OnTick()
    {
        ClockText.Text = DateTime.Now.ToString("yyyy.MM.dd  HH:mm:ss");

        // Roll the tally over at midnight.
        if (_store.RolloverIfNewDay())
        {
            RenderTally();
            Log("NEW DAY DETECTED. CYCLE TALLY RESET.");
        }

        if (_phase == Phase.Idle) return;

        var remaining = _paused ? _pausedRemaining : _phaseEnd - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            CompletePhase();
            return;
        }

        TimerText.Text = Format(remaining);
        RenderProgress(1.0 - remaining.TotalMilliseconds / _phaseLength.TotalMilliseconds);

        // Blink the readout while on hold.
        if (_paused)
            TimerText.Opacity = DateTime.Now.Millisecond < 500 ? 1.0 : 0.25;
    }

    private void BeginPhase(Phase phase)
    {
        _phase = phase;
        _paused = false;
        _phaseLength = phase == Phase.Work ? WorkDuration : BreakDuration;
        _phaseEnd = DateTime.Now + _phaseLength;

        var brush = phase == Phase.Work ? _phosphor : _amber;
        TimerText.Foreground = brush;
        PhaseText.Foreground = brush;
        TimerText.Opacity = 1;
        SetGlow(TimerText, phase == Phase.Work ? "PhosphorColor" : "AmberColor", 28);

        PhaseText.Text = phase == Phase.Work ? "STATUS: WORK CYCLE IN PROGRESS" : "STATUS: REST CYCLE // STAND DOWN";
        SubText.Text = $"SESSION {_store.Count + (phase == Phase.Work ? 1 : 0):00}  //  " +
                       (phase == Phase.Work ? "NEXT: 05 MIN REST CYCLE" : "NEXT: STANDBY");
        TimerText.Text = Format(_phaseLength);
        RenderProgress(0);

        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        AbortButton.IsEnabled = true;
        PauseButton.Content = "[ HOLD ]";
    }

    private void CompletePhase()
    {
        Flicker();
        Alarm(_phase == Phase.Work ? 3 : 2);

        if (_phase == Phase.Work)
        {
            _store.Increment();
            RenderTally();
            Log($"WORK CYCLE COMPLETE. CYCLE {_store.Count:00} LOGGED.");
            Log("COMMENCING 05 MIN REST CYCLE. CREW MAY STAND DOWN.");
            BeginPhase(Phase.Break);
        }
        else
        {
            Log("REST CYCLE COMPLETE. RETURN TO STATIONS.");
            Log("AWAITING INSTRUCTION.");
            Activate();
            ResetToIdle();
        }
    }

    private void ResetToIdle()
    {
        _phase = Phase.Idle;
        _paused = false;
        RenderIdle();
        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
        AbortButton.IsEnabled = false;
        PauseButton.Content = "[ HOLD ]";
    }

    private void RenderIdle()
    {
        TimerText.Text = Format(WorkDuration);
        TimerText.Foreground = _phosphor;
        TimerText.Opacity = 1;
        SetGlow(TimerText, "PhosphorColor", 28);
        PhaseText.Foreground = _phosphor;
        PhaseText.Text = "STATUS: STANDBY";
        SubText.Text = "25 MIN WORK CYCLE  //  05 MIN REST CYCLE";
        RenderProgress(0);
    }

    // ---------------------------------------------------------------- controls

    private void Start_Click(object sender, RoutedEventArgs e) => StartSession();
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void Abort_Click(object sender, RoutedEventArgs e) => AbortSession();

    private void StartSession()
    {
        if (_phase != Phase.Idle) return;
        Log($"WORK CYCLE {_store.Count + 1:00} INITIATED. DURATION 25:00.");
        BeginPhase(Phase.Work);
    }

    private void TogglePause()
    {
        if (_phase == Phase.Idle) return;
        if (_paused)
        {
            _phaseEnd = DateTime.Now + _pausedRemaining;
            _paused = false;
            TimerText.Opacity = 1;
            PauseButton.Content = "[ HOLD ]";
            Log("CYCLE RESUMED.");
        }
        else
        {
            _pausedRemaining = _phaseEnd - DateTime.Now;
            _paused = true;
            PauseButton.Content = "[ RESUME ]";
            Log($"CYCLE ON HOLD AT {Format(_pausedRemaining)}.");
        }
    }

    private void AbortSession()
    {
        if (_phase == Phase.Idle) return;
        Log(_phase == Phase.Work
            ? "WORK CYCLE ABORTED. CYCLE NOT LOGGED."
            : "REST CYCLE TERMINATED EARLY.");
        ResetToIdle();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: StartSession(); break;
            case Key.Space: TogglePause(); break;
            case Key.Escape: AbortSession(); break;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- rendering

    private void RenderProgress(double fraction)
    {
        if (ProgressCellsPanel.Children.Count == 0)
            for (int i = 0; i < ProgressCells; i++)
                ProgressCellsPanel.Children.Add(new Rectangle { Margin = new Thickness(1, 0, 1, 0) });

        fraction = Math.Clamp(fraction, 0, 1);
        int filled = (int)Math.Floor(fraction * ProgressCells);
        var on = _phase == Phase.Break ? _amber : _phosphor;
        var off = (Brush)FindResource("PhosphorDim");
        for (int i = 0; i < ProgressCells; i++)
        {
            var cell = (Rectangle)ProgressCellsPanel.Children[i];
            cell.Fill = i < filled ? on : off;
            cell.Opacity = i < filled ? 1.0 : 0.35;
        }
        ProgressFrame.BorderBrush = on;
        ProgressText.Text = $"{fraction * 100:0}%";
        ProgressText.Foreground = on;
    }

    /// <summary>Draws classic tally marks: four verticals and a diagonal strike per group of five.</summary>
    private void RenderTally()
    {
        TallyPanel.Children.Clear();
        int count = _store.Count;
        TallyCountText.Text = count.ToString("00");
        TallyEmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        const double h = 30, gap = 8, stroke = 3;
        for (int remaining = count; remaining > 0; remaining -= 5)
        {
            int marks = Math.Min(5, remaining);
            int verticals = Math.Min(4, marks);
            double width = (verticals - 1) * gap + stroke * 2;
            var group = new Canvas { Width = Math.Max(width, 4 * gap), Height = h, Margin = new Thickness(0, 2, 18, 2) };

            for (int i = 0; i < verticals; i++)
            {
                double x = stroke + i * gap;
                group.Children.Add(MakeLine(x, 2, x, h - 2, stroke));
            }
            if (marks == 5)
                group.Children.Add(MakeLine(-4, h - 6, 3 * gap + stroke + 4, 6, stroke));

            group.Effect = new DropShadowEffect
            {
                Color = (Color)FindResource("PhosphorColor"),
                BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9
            };
            TallyPanel.Children.Add(group);
        }
    }

    private Line MakeLine(double x1, double y1, double x2, double y2, double thickness) => new()
    {
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        Stroke = _phosphor,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Square,
        StrokeEndLineCap = PenLineCap.Square
    };

    private void SetGlow(UIElement el, string colorKey, double blur) =>
        el.Effect = new DropShadowEffect
        {
            Color = (Color)FindResource(colorKey),
            BlurRadius = blur, ShadowDepth = 0, Opacity = 0.9
        };

    private void Flicker()
    {
        var anim = new DoubleAnimationUsingKeyFrames();
        double[] values = { 0.25, 0, 0.18, 0, 0.3, 0 };
        for (int i = 0; i < values.Length; i++)
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 70))));
        FlickerLayer.BeginAnimation(OpacityProperty, anim);
    }

    private static void Alarm(int pulses) => Task.Run(() =>
    {
        try
        {
            for (int i = 0; i < pulses; i++)
            {
                Console.Beep(880, 180);
                Console.Beep(660, 180);
                Thread.Sleep(120);
            }
        }
        catch
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
    });

    private static string Format(TimeSpan t)
    {
        // Round up so 24:59.4 shows 25:00 at start and 00:01 right before the end.
        int total = (int)Math.Ceiling(t.TotalSeconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    // ---------------------------------------------------------------- typewriter log

    private void Log(string message) => _pendingLines.Enqueue("> " + message);

    private void OnType()
    {
        if (_typingPos >= _typingLine.Length && _pendingLines.Count > 0)
        {
            if (_typingLine.Length > 0) CommitLine(_typingLine);
            _typingLine = _pendingLines.Dequeue();
            _typingPos = 0;
        }

        if (_typingPos < _typingLine.Length) _typingPos++;

        if (++_cursorTicks >= 25) { _cursorTicks = 0; _cursorOn = !_cursorOn; }

        var sb = new StringBuilder();
        foreach (var l in _logLines) sb.AppendLine(l);
        sb.Append(_typingLine, 0, _typingPos);
        sb.Append(_cursorOn || _typingPos < _typingLine.Length ? "█" : " ");
        LogText.Text = sb.ToString();
    }

    private void CommitLine(string line)
    {
        _logLines.Add(line);
        while (_logLines.Count > MaxLogLines - 1) _logLines.RemoveAt(0);
    }
}

/// <summary>Persists today's completed-cycle count to %LOCALAPPDATA%\AlienPomodoro\tally.json.</summary>
internal sealed class TallyStore
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlienPomodoro", "tally.json");

    public DateTime Date { get; set; } = DateTime.Today;
    public int Count { get; set; }

    public static TallyStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonSerializer.Deserialize<TallyStore>(File.ReadAllText(FilePath));
                if (store != null)
                {
                    store.RolloverIfNewDay();
                    return store;
                }
            }
        }
        catch { /* corrupt file: start fresh */ }
        return new TallyStore();
    }

    public bool RolloverIfNewDay()
    {
        if (Date.Date == DateTime.Today) return false;
        Date = DateTime.Today;
        Count = 0;
        Save();
        return true;
    }

    public void Increment()
    {
        RolloverIfNewDay();
        Count++;
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* non-fatal */ }
    }
}
