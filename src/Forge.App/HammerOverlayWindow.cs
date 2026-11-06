using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Forge.App;

/// <summary>
/// Working indicator for the direct translate path: a small dark glass card shown over the
/// selected text while the model runs, with an animated hammer strike, an impact glow and a
/// live elapsed-time readout. The card springs in on show and pulses on each strike.
/// Topmost, click-through (WS_EX_TRANSPARENT) and non-activating
/// (WS_EX_NOACTIVATE + ShowActivated=false), so the source app keeps focus — the replace
/// step depends on it. Built in code only — no XAML (S5 convention).
/// </summary>
public sealed class HammerOverlayWindow : Window
{
    private const double CardWidth = 84;
    private const double CardHeight = 72;
    private const double ShadowPad = 20; // window padding so the card's drop shadow is not clipped
    private const double WindowWidth = CardWidth + 2 * ShadowPad;
    private const double WindowHeight = CardHeight + 2 * ShadowPad;
    private const double StrikePeriodMs = 750.0;
    private const double EntranceMs = 450.0;
    private const double LiftAngle = 38.0;

    private readonly Rect _targetRect; // screen pixels (UIA BoundingRectangle)
    private readonly DispatcherTimer _timer;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private Canvas _hammerGroup = null!;
    private Path _sparks = null!;
    private Ellipse _glow = null!;
    private TextBlock _elapsed = null!;
    private Border _card = null!;
    private bool _finishing;

    /// <summary>True after <see cref="Complete"/> — the window hides itself on a delay.</summary>
    public bool IsFinishing => _finishing;

    /// <summary>
    /// A 1x1 rectangle at the center of the primary screen (device pixels) — the fallback
    /// position when the selection's bounding rectangle can't be resolved.
    /// </summary>
    public static Rect ScreenCenter()
    {
        var cx = GetSystemMetrics(SM_CXSCREEN) / 2;
        var cy = GetSystemMetrics(SM_CYSCREEN) / 2;
        return new Rect(cx, cy, 1, 1);
    }

    /// <param name="targetRect">The selection's screen rectangle in physical pixels (UIA BoundingRectangle).</param>
    public HammerOverlayWindow(Rect targetRect)
    {
        _targetRect = targetRect;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // never take focus from the source app
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = WindowWidth;
        Height = WindowHeight;

        var canvas = new Canvas();
        _card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x44)),
            Child = canvas,
        };
        _card.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 14,
            ShadowDepth = 3,
            Opacity = 0.45,
            Direction = 90,
        };
        var root = new Canvas { Width = WindowWidth, Height = WindowHeight };
        Canvas.SetLeft(_card, ShadowPad);
        Canvas.SetTop(_card, ShadowPad);
        root.Children.Add(_card);
        Content = root;

        // Hammer: head + handle inside a group that pivots around the handle's bottom.
        var head = new Rectangle
        {
            Width = 26,
            Height = 9,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD6)),
        };
        Canvas.SetLeft(head, 29);
        Canvas.SetTop(head, 17);
        var handle = new Rectangle
        {
            Width = 7,
            Height = 26,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x4B)),
        };
        Canvas.SetLeft(handle, 38.5);
        Canvas.SetTop(handle, 24);
        _hammerGroup = new Canvas { Width = CardWidth, Height = CardHeight };
        _hammerGroup.Children.Add(head);
        _hammerGroup.Children.Add(handle);
        canvas.Children.Add(_hammerGroup);

        // Impact glow: a soft amber halo behind the strike point, fading with the impact.
        _glow = new Ellipse
        {
            Width = 30,
            Height = 30,
            Fill = ForgeTheme.AccentBrush,
            Opacity = 0,
            Effect = new BlurEffect { Radius = 8 },
        };
        Canvas.SetLeft(_glow, 42 - 15);
        Canvas.SetTop(_glow, 16 - 15);
        canvas.Children.Add(_glow);

        // Impact sparks: three short strokes radiating from the strike point (do not rotate).
        _sparks = new Path
        {
            Data = Geometry.Parse("M 42 16 L 34 7 M 42 16 L 42 5 M 42 16 L 50 7"),
            Stroke = ForgeTheme.AccentBrush,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0,
        };
        canvas.Children.Add(_sparks);

        _elapsed = new TextBlock
        {
            Width = CardWidth,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            Foreground = ForgeTheme.MutedBrush,
            Text = "0.0s",
        };
        Canvas.SetTop(_elapsed, 56);
        canvas.Children.Add(_elapsed);

        // Initial position guess (1.0 scale); corrected in OnSourceInitialized with the real DPI.
        Left = _targetRect.X + _targetRect.Width / 2 - WindowWidth / 2;
        Top = _targetRect.Y + _targetRect.Height / 2 - WindowHeight / 2;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Click-through + non-activating: the source app keeps focus and mouse input.
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

        // Position centered over the selection rectangle (UIA reports physical pixels).
        var dpi = VisualTreeHelper.GetDpi(this);
        PositionWindow(dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void PositionWindow(double dpiX, double dpiY)
    {
        var cx = _targetRect.X + _targetRect.Width / 2;
        var cy = _targetRect.Y + _targetRect.Height / 2;
        var left = (cx - CardWidth / 2) / dpiX;
        var top = (cy - CardHeight / 2) / dpiY;

        // Clamp to the virtual screen (spans all monitors), in device pixels. Skipped when
        // the metrics are degenerate (e.g. no visible display in this session) — Math.Clamp
        // throws when min > max, which would kill Show() and with it the whole run.
        var vsLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vsTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vsRight = vsLeft + GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vsBottom = vsTop + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vsRight > vsLeft && vsBottom > vsTop)
        {
            left = Math.Clamp(left, vsLeft / dpiX, (vsRight - WindowWidth) / dpiX);
            top = Math.Clamp(top, vsTop / dpiY, (vsBottom - WindowHeight) / dpiY);
        }

        Left = left;
        Top = top;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _shownAt).TotalMilliseconds;
        var phase = (t % StrikePeriodMs) / StrikePeriodMs;

        // Cycle: rest at the strike → lift → fast fall → rest.
        double angle;
        if (phase < 0.30)
            angle = 0;
        else if (phase < 0.62)
            angle = LiftAngle * EaseOutCubic((phase - 0.30) / 0.32);
        else if (phase < 0.72)
            angle = LiftAngle * (1 - EaseInQuad((phase - 0.62) / 0.10));
        else
            angle = 0;
        _hammerGroup.RenderTransform = new RotateTransform(angle) { CenterX = 42, CenterY = 50 };

        // Impact flash: the hammer lands at phase 0.72 and wraps to 0; decay over the first 15%.
        var impact = phase < 0.15 ? 1.0 - phase / 0.15 : 0.0;
        _sparks.Opacity = impact;
        _glow.Opacity = impact * 0.8;

        // Card scale: entrance spring (first EntranceMs) multiplied by the impact pulse.
        var scale = 1 + 0.05 * impact;
        if (t < EntranceMs)
            scale *= 0.6 + 0.4 * EaseOutBack(t / EntranceMs);
        _card.RenderTransform = new ScaleTransform(scale, scale) { CenterX = 42, CenterY = 36 };

        _elapsed.Text = $"{t / 1000.0:0.0}s";
    }

    /// <summary>
    /// Plays one final impact and hides the window after a short delay. Called on a
    /// successful replace, synced with the clang sound.
    /// </summary>
    public void Complete()
    {
        if (_finishing)
            return;
        _finishing = true;
        _timer.Stop();
        _sparks.Opacity = 1;
        _card.RenderTransform = new ScaleTransform(1.05, 1.05) { CenterX = 42, CenterY = 36 };
        var hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        hide.Tick += (_, _) => Close();
        hide.Start();
    }

    private static double EaseOutCubic(double u) => 1 - Math.Pow(1 - u, 3);
    private static double EaseInQuad(double u) => u * u;

    /// <summary>Back-eased spring: 0→1 with a ~10% overshoot — the card's entrance pop.</summary>
    private static double EaseOutBack(double u)
    {
        const double c1 = 1.70158;
        const double c2 = c1 + 1;
        var v = u - 1;
        return 1 + c2 * v * v * v + c1 * v * v;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
