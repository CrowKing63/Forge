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
/// Quiet notice for the clipboard fallback: a small dark glass card (same visual language as
/// <see cref="HammerOverlayWindow"/>) telling the user to paste with Ctrl+V — a clipboard glyph
/// over the instruction, mirroring the hammer card's icon-plus-text layout. Topmost,
/// click-through (WS_EX_TRANSPARENT) and non-activating (WS_EX_NOACTIVATE + ShowActivated=false),
/// so the source app keeps focus — that is what makes the Ctrl+V land there. Fades out and
/// closes itself after a few seconds. Built in code only — no XAML (S5 convention).
/// </summary>
public sealed class PasteHintWindow : Window
{
    private const double CardWidth = 150;
    private const double CardHeight = 72;
    private const double ShadowPad = 20; // window padding so the card's drop shadow is not clipped
    private const double WindowWidth = CardWidth + 2 * ShadowPad;
    private const double WindowHeight = CardHeight + 2 * ShadowPad;
    private const double DurationMs = 6000.0;
    private const double FadeMs = 400.0;
    private const double EntranceMs = 450.0;

    private readonly Rect _targetRect; // screen pixels (UIA BoundingRectangle)
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private Border _card = null!;
    private bool _closing;

    /// <param name="targetRect">The selection's screen rectangle in physical pixels (UIA BoundingRectangle).</param>
    public PasteHintWindow(Rect targetRect)
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
        // Same fake glass as the hammer card: real DWM Mica/Acrylic cannot apply to a
        // per-pixel-transparent window.
        var glassFill = new LinearGradientBrush(Color.FromRgb(0x26, 0x26, 0x2D), Color.FromRgb(0x1C, 0x1C, 0x22), 90);
        glassFill.Opacity = 0.85;
        glassFill.Freeze();
        var rimLight = new LinearGradientBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), Colors.Transparent, 90);
        rimLight.Freeze();
        _card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(10),
            Background = glassFill,
            BorderThickness = new Thickness(1),
            BorderBrush = rimLight,
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

        // Clipboard glyph: board + clip tab, same metal tones as the hammer head.
        var board = new Rectangle
        {
            Width = 22,
            Height = 26,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD6)),
        };
        Canvas.SetLeft(board, (CardWidth - 22) / 2);
        Canvas.SetTop(board, 14);
        canvas.Children.Add(board);
        var clip = new Rectangle
        {
            Width = 10,
            Height = 7,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)),
        };
        Canvas.SetLeft(clip, (CardWidth - 10) / 2);
        Canvas.SetTop(clip, 10);
        canvas.Children.Add(clip);

        var main = new TextBlock
        {
            Width = CardWidth,
            TextAlignment = TextAlignment.Center,
            FontFamily = ForgeTheme.ThemeFont,
            FontSize = 14,
            Foreground = ForgeTheme.TextBrush,
            Text = "Press Ctrl+V",
        };
        Canvas.SetTop(main, 48);
        canvas.Children.Add(main);

        var root = new Canvas { Width = WindowWidth, Height = WindowHeight };
        Canvas.SetLeft(_card, ShadowPad);
        Canvas.SetTop(_card, ShadowPad);
        root.Children.Add(_card);
        Content = root;

        // Initial position guess (1.0 scale); corrected in OnSourceInitialized with the real DPI.
        Left = _targetRect.X + _targetRect.Width / 2 - WindowWidth / 2;
        Top = _targetRect.Y + _targetRect.Height / 2 - WindowHeight / 2;

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += OnTick;
        timer.Start();
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

    private void OnTick(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _shownAt).TotalMilliseconds;

        // Entrance spring (same pop as the hammer card), settling to full opacity.
        if (t < EntranceMs)
            _card.RenderTransform = new ScaleTransform(0.6 + 0.4 * EaseOutBack(t / EntranceMs),
                0.6 + 0.4 * EaseOutBack(t / EntranceMs)) { CenterX = CardWidth / 2, CenterY = CardHeight / 2 };

        // Fade out over the last FadeMs, then close ourselves.
        if (t >= DurationMs - FadeMs)
            Opacity = Math.Max(0, (DurationMs - t) / FadeMs);
        if (t >= DurationMs && !_closing)
        {
            _closing = true;
            Close();
        }
    }

    private void PositionWindow(double dpiX, double dpiY)
    {
        var cx = _targetRect.X + _targetRect.Width / 2;
        var cy = _targetRect.Y + _targetRect.Height / 2;
        var left = (cx - CardWidth / 2) / dpiX;
        var top = (cy - CardHeight / 2) / dpiY;

        // Clamp to the virtual screen (spans all monitors), in device pixels. Skipped when
        // the metrics are degenerate — Math.Clamp throws when min > max, which would kill
        // Show() and with it the whole run.
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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
