using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// AppPaths tests: the portable marker (portable.txt next to the exe) switches the data
/// directory from %LOCALAPPDATA%\Forge to the exe's own folder.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void DataDir_IsBaseFolder_WhenPortableMarkerPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-portable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, AppPaths.PortableMarker), "");
            Assert.Equal(Path.GetFullPath(dir), AppPaths.ResolveDataDir(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DataDir_IsLocalAppData_WhenNoMarker()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-nonportable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge"),
                AppPaths.ResolveDataDir(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
