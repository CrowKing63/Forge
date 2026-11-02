using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ForgeSettingsTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("forge-settings-test").FullName;

    public void Dispose() => new DirectoryInfo(_tempDir).Delete(recursive: true);

    [Fact]
    public void Defaults_MatchEngineDefaults()
    {
        var settings = ForgeSettings.Load(Path.Combine(_tempDir, "settings.json"));

        Assert.Equal(8192, settings.ContextSize);
        Assert.Equal(HardwareMode.Cpu, settings.Hardware);
        Assert.False(string.IsNullOrWhiteSpace(settings.ModelPath));
        Assert.Equal(PromptBuilder.SystemPrompt(ForgeTask.Proofread), settings.Prompts[ForgeTask.Proofread.ToString()]);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var settings = new ForgeSettings
        {
            ModelPath = @"C:\models\test.gguf",
            ContextSize = 4096,
            Hardware = HardwareMode.Gpu,
        };
        settings.Prompts[ForgeTask.Summarize.ToString()] = "Custom prompt.";

        settings.Save(path);
        var loaded = ForgeSettings.Load(path);

        Assert.Equal(settings.ModelPath, loaded.ModelPath);
        Assert.Equal(settings.ContextSize, loaded.ContextSize);
        Assert.Equal(settings.Hardware, loaded.Hardware);
        Assert.Equal("Custom prompt.", loaded.Prompts[ForgeTask.Summarize.ToString()]);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = ForgeSettings.Load(Path.Combine(_tempDir, "does-not-exist.json"));

        Assert.Equal(8192, settings.ContextSize);
        Assert.Equal(HardwareMode.Cpu, settings.Hardware);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(path, "{ not valid json !!!");

        var settings = ForgeSettings.Load(path);

        Assert.Equal(8192, settings.ContextSize);
    }
}
