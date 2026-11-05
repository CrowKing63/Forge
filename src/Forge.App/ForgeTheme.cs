using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public static readonly SolidColorBrush TextBrush = Brush(0xE8, 0xE8, 0xEC);
    public static readonly SolidColorBrush MutedBrush = Brush(0x9A, 0x9A, 0xA5);
    public static readonly SolidColorBrush AccentBrush = Brush(0xFF, 0xC4, 0x6B);
    private static readonly SolidColorBrush AccentHoverBrush = Brush(0xE8, 0xB2, 0x5C);
    private static readonly SolidColorBrush AccentPressedBrush = Brush(0xD4, 0xA0, 0x4E);

    /// <summary>Builds the theme's ResourceDictionary for a window's Resources.</summary>
    public static ResourceDictionary Build()
    {
        var resources = new ResourceDictionary();
        resources.Add(typeof(TextBlock), TextBlockStyle());
        resources.Add("HintText", HintTextStyle());
        resources.Add(typeof(TextBox), TextBoxStyle());
        // Keyed by the exact runtime type: WPF implicit styles do not walk the type
        // hierarchy, so a typeof(ComboBox) key would never match ThemedComboBox.
        resources.Add(typeof(ThemedComboBox), ComboBoxStyle());
        resources.Add(typeof(ComboBoxItem), ComboBoxItemStyle());
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
            new Setter(Controls.Control.FontSizeProperty, 12.0),
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

    private static Style ComboBoxStyle()
    {
        // Color-only style: no Template setter. WPF only attaches its dropdown
        // infrastructure (chrome > Border > ScrollViewer > ItemsPresenter) to the
        // default template's PART_Popup; an FEF-built popup is never recognized, so a
        // custom template leaves the list unable to render or scroll. The default
        // template is themed in place via ThemedComboBox.OnApplyTemplate.
        return new Style(typeof(ThemedComboBox))
        {
            Setters =
            {
                new Setter(Controls.Control.BackgroundProperty, FieldBrush),
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.BorderBrushProperty, BorderBrush),
                new Setter(ItemsControl.ItemContainerStyleProperty, ComboBoxItemStyle()),
            },
        };
    }

    private static Style ComboBoxItemStyle()
    {
        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Controls.Control.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(label);

        var style = new Style(typeof(ComboBoxItem))
        {
            Setters =
            {
                new Setter(Controls.Control.ForegroundProperty, TextBrush),
                new Setter(Controls.Control.TemplateProperty, new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border }),
            },
        };
        var highlighted = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        highlighted.Setters.Add(new Setter(Controls.Control.BackgroundProperty, HoverFieldBrush));
        style.Triggers.Add(highlighted);
        var selected = new Trigger { Property = Controls.ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Controls.Control.BackgroundProperty, BorderBrush));
        style.Triggers.Add(selected);
        return style;
    }
}

/// <summary>
/// ComboBox themed on top of the default template: the style only sets colors, so
/// WPF's dropdown infrastructure (chrome > Border > ScrollViewer > ItemsPresenter)
/// stays intact and is re-sketched in place when the popup opens.
/// </summary>
public sealed class ThemedComboBox : Controls.ComboBox
{
    public ThemedComboBox()
    {
        // The default template's field Border is TemplateBinding-driven, so pinning
        // the brushes here themes the closed-state field even if the style's color
        // setters fail to apply.
        Background = ForgeTheme.FieldBrush;
        Foreground = ForgeTheme.TextBrush;
        BorderBrush = ForgeTheme.BorderBrush;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // The default template's editable-text Border is hardcoded to
        // SystemColors.WindowBrushKey (white) and covers the whole closed-state
        // field, hiding the dark toggle button behind it. Make it transparent.
        if (GetTemplateChild("border") is Border border)
            border.Background = Brushes.Transparent;

        // The toggle button's border uses a hardcoded theme gradient
        // (LinearGradientBrush) that ignores our Background setter. Replace it
        // after layout so the visual tree exists.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var b in EnumerateBorders(this))
                if (b.Background is LinearGradientBrush)
                    b.Background = ForgeTheme.FieldBrush;
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        if (GetTemplateChild("PART_Popup") is not Popup popup) return;

        // WPF builds the dropdown infrastructure (drop-shadow chrome > Border >
        // ScrollViewer > ItemsPresenter) lazily when the popup opens, and the
        // scroll viewer only exists after layout — so theme at Loaded priority.
        // The popup's content lives in a separate visual tree (its own HwndSource),
        // so the walk must start at popup.Child, not at the Popup element itself.
        popup.Opened += (_, _) =>
        {
            popup.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (popup.Child is not DependencyObject root) return;
                var border = FindVisual<Border>(root);
                if (border is not null)
                {
                    border.Background = ForgeTheme.CardBrush;
                    border.BorderBrush = ForgeTheme.BorderBrush;
                    border.MaxHeight = 300;
                }
                var scroll = FindVisual<ScrollViewer>(root);
                if (scroll is not null)
                {
                    scroll.Background = Brushes.Transparent;
                    // Pixel scrolling: with a non-virtualizing StackPanel host, WPF's default
                    // item-based mode clamps the list to the viewport and breaks scrolling.
                    scroll.CanContentScroll = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindVisual<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<Border> EnumerateBorders(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border b) yield return b;
            foreach (var nested in EnumerateBorders(child)) yield return nested;
        }
    }
}
