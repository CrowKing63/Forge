using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ForgeSettingsTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.GetTempFileName();
        File.Delete(path);
        var s = ForgeSettings.Load(path);
        Assert.Equal(8192, s.ContextSize);
        Assert.Equal(ModelUnloadAfter.OneMinute, s.UnloadAfter);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var path = Path.GetTempFileName();
        var s = ForgeSettings.Load(path);
        s.ContextSize = 4096;
        s.UnloadAfter = ModelUnloadAfter.FiveMinutes;
        s.Save(path);

        var loaded = ForgeSettings.Load(path);
        Assert.Equal(4096, loaded.ContextSize);
        Assert.Equal(ModelUnloadAfter.FiveMinutes, loaded.UnloadAfter);
    }

    [Fact]
    public void Load_OldFileWithIdleUnloadSeconds_IgnoresUnknownKey()
    {
        // S17 migration: settings.json files written before the UnloadAfter enum
        // carry "IdleUnloadSeconds"; unknown keys must deserialize to defaults.
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "ModelPath": "x.gguf",
          "ContextSize": 8192,
          "IdleUnloadSeconds": 120,
          "Hardware": "Cpu",
          "CrashDumpEnabled": false
        }
        """);

        var loaded = ForgeSettings.Load(path);
        Assert.Equal(ModelUnloadAfter.OneMinute, loaded.UnloadAfter);
    }
}
