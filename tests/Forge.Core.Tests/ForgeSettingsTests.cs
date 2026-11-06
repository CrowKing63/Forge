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
        Assert.Equal(4096, s.ContextSize);
        Assert.Equal(30, s.IdleUnloadSeconds);
        Assert.Equal(HardwareMode.Auto, s.Hardware);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var path = Path.GetTempFileName();
        var s = ForgeSettings.Load(path);
        s.ContextSize = 8192;
        s.IdleUnloadSeconds = 60;
        s.Hardware = HardwareMode.Cpu;
        s.Save(path);

        var loaded = ForgeSettings.Load(path);
        Assert.Equal(8192, loaded.ContextSize);
        Assert.Equal(60, loaded.IdleUnloadSeconds);
        Assert.Equal(HardwareMode.Cpu, loaded.Hardware);
    }
}
