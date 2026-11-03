using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S12 CrashDump tests: the dump records diagnostics (timestamp, runtime, model path,
/// GPU layer count, exception stack) and structurally cannot carry text content —
/// its API accepts no text at all (plan §4 privacy).
/// </summary>
public class CrashDumpTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("forge-crash-dump-test").FullName;

    public void Dispose() => new DirectoryInfo(_tempDir).Delete(recursive: true);

    [Fact]
    public void Write_ProducesDiagnosticFile()
    {
        var ex = new InvalidOperationException("boom");
        string? file = CrashDump.Write(ex, @"C:\models\test.gguf", 99);

        Assert.NotNull(file);
        string content = File.ReadAllText(file!);
        Assert.Contains("boom", content);
        Assert.Contains(@"C:\models\test.gguf", content);
        Assert.Contains("gpu layer count: 99", content);
        Assert.Contains("runtime:", content);
        Assert.StartsWith("Forge crash dump — ", content);
    }

    [Fact]
    public void Write_NeverIncludesTextPayload()
    {
        // The contract under test: a text payload that was never passed to the API
        // cannot appear in the dump. (The stronger guarantee is structural — the
        // method signature has no text parameter.)
        const string payload = "SECRET-TEXT-PAYLOAD-12345";
        var ex = new InvalidOperationException("boom");
        string? file = CrashDump.Write(ex, @"C:\models\test.gguf", 0);

        Assert.NotNull(file);
        Assert.DoesNotContain(payload, File.ReadAllText(file!));
    }

    [Fact]
    public void Write_FileNameCarriesTimestamp()
    {
        var ex = new InvalidOperationException("boom");
        string? file = CrashDump.Write(ex, "m.gguf", 0);

        Assert.NotNull(file);
        string name = Path.GetFileName(file!);
        Assert.StartsWith("crash-", name);
        Assert.EndsWith(".log", name);
    }
}
