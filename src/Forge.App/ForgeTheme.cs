using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Controls = System.Windows.Controls;
using Shapes = System.Windows.Shapes;

namespace Forge.App;

/// <summary>
/// Forge's dark theme for its windows (code-only WPF, S5 convention — no XAML files).
/// Builds a ResourceDictionary of implicit styles and control templates matching the
/// blacksmith identity: charcoal surfaces with an amber accent, the same palette as the
/// hammer overlay card. A window opts in via <c>Resources = ForgeTheme.Build()</c>.
/// </summary>
public static class ForgeTheme
{
    // Palette — anchored on HammerOverlayWindow's card (#1E1E24) and spark (#FFC46B).
    public static readonly SolidColorBrush BackgroundBrush = Brush(0x1B, 0x1B, 0x20);
    public static readonly SolidColorBrush CardBrush = Brush(0x23, 0x23, 0x29);
    public static readonly SolidColorBrush FieldBrush = Brush(0x2A, 0x2A, 0x31);
    public static readonly SolidColorBrush HoverFieldBrush = Brush(0x33, 0x33, 0x3B);
    public static readonly SolidColorBrush BorderBrush = Brush(0x3A, 0x3A, 0x44);
    public static readonly SolidColorBrush TextBrush = Brush(0xF4, 0xF4, 0xF7);
    public static readonly SolidColorBrush MutedBrush = Brush(0xB2, 0xB2, 0xBC);
    public static readonly SolidColorBrush AccentBrush = Brush(0xFF, 0xC4, 0x6B);
    private static readonly SolidColorBrush AccentHoverBrush = Brush(0xE8, 0xB2, 0x5C);
    private static readonly SolidColorBrush AccentPressedBrush = Brush(0xD4, 0xA0, 0x4E);

    /// <summary>
    /// The theme's typeface: Segoe UI Variable (Win11's variable-weight face — the closest
    /// system match to the minimal modern look) with plain Segoe UI as the Win10 fallback.
    /// </summary>
    public static readonly FontFamily ThemeFont = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>Builds the theme's ResourceDictionary for a window's Resources.</summary>
    public static ResourceDictionary Build()
    {
        var resources = new ResourceDictionary();
        resources.Add(typeof(TextBlock), TextBlockStyle());
        resources.Add("HintText", HintTextStyle());
        resources.Add("BodyText", BodyTextStyle());
        resources.Add(typeof(TextBox), TextBoxStyle());
        resources.Add(typeof(CheckBox), CheckBoxStyle());
        resources.Add(typeof(RadioButton), RadioButtonStyle());
        resources.Add(typeof(Button), ButtonStyle());
        resources.Add("AccentButton", AccentButtonStyle());
        return resources;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Style TextBlockStyle() => new(typeof(TextBlock))
    {
        Setters = { new Setter(Controls.Control.ForegroundProperty, TextBrush) },
    };

    /// <summary>Muted secondary copy: descriptions and hints.</summary>
    private static Style HintTextStyle() => new(typeof(TextBlock))
    {
        Setters =
        {
            new Setter(TextBlock.ForegroundProperty, MutedBrush),
            new Setter(Controls.Control.FontSizeProperty, 13.0),
            new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
        },
    };

    /// <summary>Primary body copy at the theme's base size (full-brightness text).</summary>
    private static Style BodyTextStyle() => new(typeof(TextBlock))
    {
        Setters =
        {
            new Setter(TextBlock.ForegroundProperty, TextBrush),
            new Setter(Controls.Control.FontSizeProperty, 14.0),
            new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
        },
    };

    private static Style TextBoxStyle()
    {
        var style = new Style(typeof(TextBox))
        {
            Setters =
            {
                new Setter(Controls.Control.BackgroundProperty, FieldBrush),
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.BorderBrushProperty, BorderBrush),
                new Setter(Controls.Control.PaddingProperty, new Thickness(8, 6, 8, 6)),
                new Setter(TextBox.CaretBrushProperty, TextBrush),
                new Setter(TextBox.SelectionBrushProperty, AccentBrush),
            },
        };
        var focus = new Trigger { Property = Controls.Control.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, AccentBrush));
        style.Triggers.Add(focus);
        return style;
    }

    private static Style ButtonStyle()
    {
        var style = new Style(typeof(Button))
        {
            Setters =
            {
                new Setter(Controls.Control.BackgroundProperty, FieldBrush),
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.BorderBrushProperty, BorderBrush),
                new Setter(Controls.Control.PaddingProperty, new Thickness(14, 7, 14, 7)),
            },
        };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Controls.Control.BackgroundProperty, HoverFieldBrush));
        style.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Controls.Control.BackgroundProperty, BorderBrush));
        style.Triggers.Add(pressed);
        return style;
    }

    /// <summary>Primary action button: amber fill with dark text.</summary>
    private static Style AccentButtonStyle()
    {
        var style = new Style(typeof(Button))
        {
            Setters =
            {
                new Setter(Controls.Control.BackgroundProperty, AccentBrush),
                new Setter(Controls.Control.ForegroundProperty, BackgroundBrush),
                new Setter(Controls.Control.BorderBrushProperty, Brushes.Transparent),
                new Setter(Controls.Control.PaddingProperty, new Thickness(16, 7, 16, 7)),
                new Setter(Controls.Control.FontWeightProperty, FontWeights.SemiBold),
            },
        };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Controls.Control.BackgroundProperty, AccentHoverBrush));
        style.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Controls.Control.BackgroundProperty, AccentPressedBrush));
        style.Triggers.Add(pressed);
        return style;
    }

    private static Style CheckBoxStyle()
    {
        var check = new FrameworkElementFactory(typeof(Shapes.Path)) { Name = "Check" };
        check.SetValue(Shapes.Path.DataProperty, Geometry.Parse("M 3 8 L 7 12 L 13 4"));
        check.SetValue(Shape.StrokeProperty, BackgroundBrush);
        check.SetValue(Shape.StrokeThicknessProperty, 2.0);
        check.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetValue(FrameworkElement.VisibilityProperty, Visibility.Collapsed);

        var box = new FrameworkElementFactory(typeof(Border));
        box.SetValue(FrameworkElement.WidthProperty, 16.0);
        box.SetValue(FrameworkElement.HeightProperty, 16.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        box.SetValue(Controls.Control.BackgroundProperty, FieldBrush);
        box.SetValue(Controls.Control.BorderBrushProperty, BorderBrush);
        box.SetValue(Controls.Control.BorderThicknessProperty, new Thickness(1));
        box.AppendChild(check);

        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.AppendChild(box);
        panel.AppendChild(label);

        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = panel };

        var isChecked = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(FrameworkElement.VisibilityProperty, Visibility.Visible) { TargetName = "Check" });
        isChecked.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, AccentBrush));
        template.Triggers.Add(isChecked);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(FrameworkElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);

        return new Style(typeof(CheckBox))
        {
            Setters =
            {
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.BorderBrushProperty, BorderBrush),
                new Setter(Controls.Control.TemplateProperty, template),
            },
        };
    }

    private static Style RadioButtonStyle()
    {
        var dot = new FrameworkElementFactory(typeof(Ellipse)) { Name = "Dot" };
        dot.SetValue(FrameworkElement.WidthProperty, 8.0);
        dot.SetValue(FrameworkElement.HeightProperty, 8.0);
        dot.SetValue(Shape.FillProperty, AccentBrush);
        dot.SetValue(FrameworkElement.VisibilityProperty, Visibility.Collapsed);

        var box = new FrameworkElementFactory(typeof(Border));
        box.SetValue(FrameworkElement.WidthProperty, 16.0);
        box.SetValue(FrameworkElement.HeightProperty, 16.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        box.SetValue(Controls.Control.BackgroundProperty, FieldBrush);
        box.SetValue(Controls.Control.BorderBrushProperty, BorderBrush);
        box.SetValue(Controls.Control.BorderThicknessProperty, new Thickness(1));
        box.AppendChild(dot);

        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.AppendChild(box);
        panel.AppendChild(label);

        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = panel };

        var isChecked = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(FrameworkElement.VisibilityProperty, Visibility.Visible) { TargetName = "Dot" });
        isChecked.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, AccentBrush));
        template.Triggers.Add(isChecked);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(FrameworkElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);

        return new Style(typeof(RadioButton))
        {
            Setters =
            {
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.BorderBrushProperty, BorderBrush),
                new Setter(Controls.Control.TemplateProperty, template),
            },
        };
    }

}

