using System.Windows;
using Forge.App;
using Xunit;

namespace Forge.App.Tests;

/// <summary>
/// Theme regression tests. WPF validates setter values when a Style is sealed — which
/// happens at runtime, the first time a styled control enters the visual tree (e.g. the
/// settings window's first tab containing a Button or TextBox). An invalid value such as an
/// empty ControlTemplate for FocusVisualStyle therefore crashes the app there, not at build
/// time. Sealing every theme style headlessly catches that class of bug.
/// </summary>
public class ForgeThemeTests
{
    [Fact]
    public void ThemeStyles_Seal_WithoutError()
    {
        var resources = ForgeTheme.Build();
        foreach (System.Collections.DictionaryEntry entry in resources)
        {
            if (entry.Value is Style style)
                style.Seal(); // throws ArgumentException when a setter value is invalid
        }
    }
}
