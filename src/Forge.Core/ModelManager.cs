using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Forge.Core;

/// <summary>
/// Metadata for one model in the catalog. The built-in default entry carries a known
/// SHA256 and size; user-dropped GGUFs (discovered from the models directory) have no
/// verified checksum and no download URL.
/// </summary>
/// <param name="Id">Unique key; also the file name without extension (<c>&lt;id&gt;.gguf</c>).</param>
/// <param name="Name">Display name for UI.</param>
/// <param name="Quantization">Quantization tag (e.g. "UD-Q4_K_XL"); empty when unknown.</param>
/// <param name="SizeBytes">Expected file size in bytes; 0 when unknown.</param>
/// <param name="Sha256">Expected SHA256 (lowercase hex); empty for unverified user files.</param>
/// <param name="DownloadUrl">Download URL; empty for local-only user files.</param>
public sealed record ModelInfo(
    string Id,
    string Name,
    string Quantization,
    long SizeBytes,
    string Sha256,
    string DownloadUrl);

/// <summary>Raised when a downloaded file's SHA256 does not match the catalog entry.</summary>
public sealed class ChecksumMismatchException : Exception
{
    public ChecksumMismatchException(string message) : base(message) { }
}

/// <summary>Raised when the disk has less free space than the model requires.</summary>
public sealed class InsufficientDiskSpaceException : Exception
{
    public InsufficientDiskSpaceException(string message) : base(message) { }
}

/// <summary>Download progress: bytes received so far and the expected total.</summary>
public sealed record DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Fraction of the download complete (0..1); 0 when the total is unknown.</summary>
    public double Fraction => TotalBytes > 0 ? (double)BytesReceived / TotalBytes : 0;
}

/// <summary>
/// Downloads and manages GGUF model files under a models directory
/// (<c>%LOCALAPPDATA%\Forge\Models\*.gguf</c>).
///
/// Catalog: the built-in default (Gemma 4 E2B UD-Q4_K_XL) plus any user GGUFs already
/// present in the directory. Downloads are resumable (HTTP Range), verified against the
/// catalog SHA256, and guarded by a free-disk-space check. A download writes to
/// <c>&lt;id&gt;.gguf.part</c> and is renamed to the final name only after verification
/// passes, so an interrupted or corrupt download never masquerades as a model.
///
/// The selected model's path is written through <see cref="ForgeSettings.ModelPath"/>
/// — the single source of truth that <see cref="InferenceEngine.LoadModel"/> consumes.
/// </summary>
public sealed class ModelManager
{
    /// <summary>Id of the built-in default model.</summary>
    public const string DefaultModelId = "gemma-4-e2b-qat-ud-q4_k_xl";

    /// <summary>
    /// The built-in default: Gemma 4 E2B QAT, UD-Q4_K_XL quant. Checksum and size verified
    /// against the S0-verified file in the HuggingFace cache.
    /// </summary>
    public static readonly ModelInfo DefaultModel = new(
        DefaultModelId,
        "Gemma 4 E2B (QAT)",
        "UD-Q4_K_XL",
        2_620_370_976,
        "e531007218dfab990486a5de7676a6932d6ea8dea233d1f698d7c21cf8a16889",
        "https://huggingface.co/unsloth/gemma-4-E2B-it-qat-GGUF/resolve/main/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf");

    /// <summary>
    /// Models in the data directory (<see cref="AppPaths.DataDir"/>): under
    /// %LOCALAPPDATA%\Forge normally, or next to the exe in portable mode.
    /// </summary>
    public static string DefaultModelsDir => Path.Combine(AppPaths.DataDir, "Models");

    private static readonly int ReadBufferSize = 64 * 1024;

    private readonly string _modelsDir;
    private readonly HttpClient _http;

    public ModelManager(string modelsDir, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(modelsDir))
            throw new ArgumentException("Models directory must not be empty.", nameof(modelsDir));
        _modelsDir = Path.GetFullPath(modelsDir);
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// The catalog: the built-in default plus every user GGUF already in the models
    /// directory (scanned by file name; unverified entries have no checksum/URL).
    /// </summary>
    public IReadOnlyList<ModelInfo> ListModels()
    {
        var models = new List<ModelInfo> { DefaultModel };
        if (Directory.Exists(_modelsDir))
        {
            foreach (var file in Directory.EnumerateFiles(_modelsDir, "*.gguf"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (models.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                models.Add(new ModelInfo(
                    Id: id,
                    Name: id,
                    Quantization: "",
                    SizeBytes: new FileInfo(file).Length,
                    Sha256: "",
                    DownloadUrl: ""));
            }
        }
        return models;
    }

    /// <summary>The final file path for a model inside the models directory.</summary>
    public string GetPath(ModelInfo model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        return Path.Combine(_modelsDir, model.Id + ".gguf");
    }

    /// <summary>True when the model file already exists on disk.</summary>
    public bool IsDownloaded(ModelInfo model) => File.Exists(GetPath(model));

    /// <summary>
    /// Downloads <paramref name="model"/> into the models directory, resuming from any
    /// existing <c>.part</c> file. Verifies the SHA256 (when known) and renames to the
    /// final name only on success. A no-op when the final file already exists.
    /// </summary>
    /// <exception cref="InsufficientDiskSpaceException">Free space is below the model size.</exception>
    /// <exception cref="ChecksumMismatchException">The downloaded file's SHA256 does not match.</exception>
    /// <exception cref="IOException">The HTTP request failed (non-success status).</exception>
    public async Task DownloadAsync(
        ModelInfo model,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(model.DownloadUrl))
            throw new ArgumentException("Model has no download URL.", nameof(model));

        string finalPath = GetPath(model);
        if (File.Exists(finalPath)) return;

        Directory.CreateDirectory(_modelsDir);

        if (model.SizeBytes > 0 && FreeSpace() < model.SizeBytes)
            throw new InsufficientDiskSpaceException(
                $"Not enough disk space for {model.Id}: need {model.SizeBytes / (1024 * 1024)} MB, " +
                $"only {FreeSpace() / (1024 * 1024)} MB free on {Path.GetFullPath(_modelsDir)}.");

        string partPath = finalPath + ".part";
        long received = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        // A .part larger than the expected size is corrupt — start over.
        if (model.SizeBytes > 0 && received > model.SizeBytes)
        {
            File.Delete(partPath);
            received = 0;
        }

        HttpResponseMessage response = await OpenDownloadAsync(model, received, cancellationToken).ConfigureAwait(false);

        // Server ignored the Range header (200 instead of 206): restart from zero.
        if (received > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(partPath);
            received = 0;
            response.Dispose();
            response = await OpenDownloadAsync(model, 0, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
                throw new IOException($"Download failed for {model.Id}: HTTP {(int)response.StatusCode}.");

            long total = model.SizeBytes > 0
                ? model.SizeBytes
                : received + (response.Content.Headers.ContentLength ?? 0);

            long got = received;
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var file = new FileStream(
                    partPath,
                    received == 0 ? FileMode.Create : FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);

                var buffer = new byte[ReadBufferSize];
                while (true)
                {
                    int n = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (n == 0) break;
                    file.Write(buffer, 0, n);
                    got += n;
                    progress?.Report(new DownloadProgress(got, total));
                }
            }

            // The write stream is closed before hashing — the file is complete and unshared.
            string actualSha256 = await Sha256FileAsync(partPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(model.Sha256) &&
                !string.Equals(actualSha256, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partPath);
                throw new ChecksumMismatchException(
                    $"SHA256 mismatch for {model.Id}: expected {model.Sha256}, got {actualSha256}. " +
                    "The download was discarded.");
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(partPath, finalPath);
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>Writes the selected model's path into settings (the load target for S1).</summary>
    public void Select(ModelInfo model, ForgeSettings settings)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        settings.ModelPath = GetPath(model);
    }

    private long FreeSpace() => new DriveInfo(_modelsDir).AvailableFreeSpace;

    private Task<HttpResponseMessage> OpenDownloadAsync(ModelInfo model, long received, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, model.DownloadUrl);
        if (received > 0)
            request.Headers.Range = new RangeHeaderValue(received, null);
        return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
