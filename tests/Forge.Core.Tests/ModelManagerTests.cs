using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S9 ModelManager: catalog listing, resumable download, SHA256 verification, and disk-space
/// guarding. Driven with a fake HTTP handler — no network, no real models.
/// </summary>
public class ModelManagerTests : IDisposable
{
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value); // inline, no thread-pool marshaling
    }

    private static HttpResponseMessage Response(byte[] payload, HttpStatusCode status = HttpStatusCode.PartialContent)
    {
        return new HttpResponseMessage(status) { Content = new ByteArrayContent(payload) };
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "forge-model-manager-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task DownloadsFullFileAndVerifiesChecksum()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var model = new ModelInfo("test-model", "Test", "Q4_0", payload.Length, Sha256(payload), "http://fake/model.gguf");
        var handler = new FakeHttpHandler(_ => Response(payload));
        var manager = new ModelManager(_dir, new HttpClient(handler));

        await manager.DownloadAsync(model);

        string path = manager.GetPath(model);
        Assert.True(File.Exists(path));
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.True(manager.IsDownloaded(model));
    }

    [Fact]
    public async Task ResumesFromExistingPartFile()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        long half = payload.Length / 2;
        var model = new ModelInfo("test-model", "Test", "Q4_0", payload.Length, Sha256(payload), "http://fake/model.gguf");

        // Simulate an interrupted download: first half already on disk.
        File.WriteAllBytes(Path.Combine(_dir, model.Id + ".gguf.part"), payload.AsSpan(0, (int)half).ToArray());

        FakeHttpHandler handler;
        handler = new FakeHttpHandler(request =>
        {
            // The resume request must carry Range: bytes=<half>-.
            Assert.Equal(new RangeHeaderValue(half, null), request.Headers.Range);
            return Response(payload.AsSpan((int)half, (int)(payload.Length - half)).ToArray());
        });
        var manager = new ModelManager(_dir, new HttpClient(handler));

        await manager.DownloadAsync(model);

        string path = Path.Combine(_dir, model.Id + ".gguf");
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.Single(handler.Requests); // exactly one request — the resume
    }

    [Fact]
    public async Task RestartsWhenServerIgnoresRange()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        long half = payload.Length / 2;
        var model = new ModelInfo("test-model", "Test", "Q4_0", payload.Length, Sha256(payload), "http://fake/model.gguf");
        File.WriteAllBytes(Path.Combine(_dir, model.Id + ".gguf.part"), payload.AsSpan(0, (int)half).ToArray());

        var handler = new FakeHttpHandler(request =>
            request.Headers.Range is null
                ? Response(payload, HttpStatusCode.OK)          // second request: full restart
                : Response(payload, HttpStatusCode.OK));      // first request: server ignored Range
        var manager = new ModelManager(_dir, new HttpClient(handler));

        await manager.DownloadAsync(model);

        string path = Path.Combine(_dir, model.Id + ".gguf");
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CorruptPartLargerThanExpectedIsRestarted()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var model = new ModelInfo("test-model", "Test", "Q4_0", payload.Length, Sha256(payload), "http://fake/model.gguf");
        File.WriteAllBytes(Path.Combine(_dir, model.Id + ".gguf.part"), new byte[] { 9, 9, 9, 9, 9 }); // larger than expected

        var handler = new FakeHttpHandler(request =>
        {
            Assert.Null(request.Headers.Range); // must start over without a Range header
            return Response(payload);
        });
        var manager = new ModelManager(_dir, new HttpClient(handler));

        await manager.DownloadAsync(model);

        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_dir, model.Id + ".gguf")));
    }

    [Fact]
    public async Task ChecksumMismatchDeletesPartAndThrows()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var model = new ModelInfo("bad-model", "Bad", "Q4_0", payload.Length, "0000000000000000000000000000000000000000000000000000000000000000", "http://fake/model.gguf");
        var manager = new ModelManager(_dir, new HttpClient(new FakeHttpHandler(_ => Response(payload))));

        var ex = await Assert.ThrowsAsync<ChecksumMismatchException>(() => manager.DownloadAsync(model));
        Assert.Contains("SHA256 mismatch", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, model.Id + ".gguf")));
        Assert.False(File.Exists(Path.Combine(_dir, model.Id + ".gguf.part")));
    }

    [Fact]
    public async Task InsufficientDiskSpaceThrowsBeforeAnyRequest()
    {
        // 10 TB — no machine has that free. The handler must never be called.
        var model = new ModelInfo("huge-model", "Huge", "Q4_0", 10_000_000_000_000, Sha256(new byte[1]), "http://fake/model.gguf");
        var handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not download"));
        var manager = new ModelManager(_dir, new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InsufficientDiskSpaceException>(() => manager.DownloadAsync(model));
        Assert.Contains("Not enough disk space", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadIsNoOpWhenFinalFileExists()
    {
        var model = new ModelInfo("done-model", "Done", "Q4_0", 1, Sha256(new byte[] { 1 }), "http://fake/model.gguf");
        File.WriteAllBytes(Path.Combine(_dir, model.Id + ".gguf"), new byte[] { 1 });
        var handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not download"));
        var manager = new ModelManager(_dir, new HttpClient(handler));

        await manager.DownloadAsync(model); // must not touch the network

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(_dir, model.Id + ".gguf")));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ListModelsIncludesBuiltInAndUserGgufs()
    {
        File.WriteAllBytes(Path.Combine(_dir, "my-user-model.gguf"), new byte[] { 1, 2, 3 });
        var manager = new ModelManager(_dir);

        var models = manager.ListModels();

        Assert.Contains(models, m => m.Id == ModelManager.DefaultModelId);
        var user = models.Single(m => m.Id == "my-user-model");
        Assert.Equal(3, user.SizeBytes);
        Assert.Equal(string.Empty, user.Sha256);
        Assert.Equal(string.Empty, user.DownloadUrl);
    }

    [Fact]
    public void ListModelsDoesNotDuplicateBuiltInId()
    {
        File.WriteAllBytes(Path.Combine(_dir, ModelManager.DefaultModelId + ".gguf"), new byte[] { 1 });
        var manager = new ModelManager(_dir);

        Assert.Single(manager.ListModels(), m => m.Id == ModelManager.DefaultModelId);
    }

    [Fact]
    public void SelectWritesModelPathIntoSettings()
    {
        var manager = new ModelManager(_dir);
        var settings = new ForgeSettings();

        manager.Select(ModelManager.DefaultModel, settings);

        Assert.Equal(Path.Combine(_dir, ModelManager.DefaultModelId + ".gguf"), settings.ModelPath);
    }

    [Fact]
    public async Task ProgressReportsGrowingByteCount()
    {
        var payload = Enumerable.Range(1, 2048).Select(i => (byte)i).ToArray();
        var model = new ModelInfo("prog-model", "Prog", "Q4_0", payload.Length, Sha256(payload), "http://fake/model.gguf");
        var progress = new List<DownloadProgress>();
        var manager = new ModelManager(_dir, new HttpClient(new FakeHttpHandler(_ => Response(payload))));

        await manager.DownloadAsync(model, progress: new SyncProgress<DownloadProgress>(progress.Add));

        Assert.NotEmpty(progress);
        Assert.Equal(payload.Length, progress[^1].BytesReceived);
        Assert.Equal(1.0, progress[^1].Fraction);
    }
}
