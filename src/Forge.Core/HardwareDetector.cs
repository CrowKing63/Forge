using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// Snapshot of the machine's inference hardware, as seen by Forge at startup.
/// </summary>
/// <param name="GpuUsable">True when a GPU backend is usable for layer offload in this build.</param>
/// <param name="GpuName">GPU display name (e.g. "NVIDIA GeForce RTX 5070 Ti"); null when unknown.</param>
/// <param name="VramMb">Total VRAM in MB; 0 when unknown.</param>
/// <param name="CudaMajorVersion">-1 when not queried (the FFI build does not read the CUDA toolkit version; the authoritative signal is <see cref="GpuUsable"/>).</param>
/// <param name="VulkanVersion">Vulkan loader version; null when absent. Informational only — Forge does not bundle a Vulkan runtime.</param>
/// <param name="Note">Human-readable status for logs and the settings surface.</param>
public sealed record HardwareInfo(
    bool GpuUsable,
    string? GpuName,
    int VramMb,
    int CudaMajorVersion,
    string? VulkanVersion,
    string Note);

/// <summary>
/// Detects the GPU available for inference offload and recommends a <c>GpuLayerCount</c>.
///
/// CPU-first: the recommendation is 0 (CPU) unless a usable CUDA backend exists. Detection
/// is best-effort by design — every probe is wrapped so a missing driver, toolkit, or native
/// library yields "no usable GPU" instead of a crash.
///
/// Detection loads all ggml backends (via <see cref="LlamaNative.EnsureBackendsLoaded"/>) and
/// then enumerates the devices they expose, so it must run before any model load — the app
/// calls it once at startup.
/// </summary>
public static class HardwareDetector
{
    /// <summary>
    /// Probes the machine: OS-level GPU name + VRAM (nvidia-smi), and — authoritatively —
    /// whether the loaded runtime exposes a non-CPU device. Never throws.
    /// </summary>
    public static HardwareInfo Detect()
    {
        var (gpuName, vramMb) = TryQueryNvidiaSmi();

        // Load every available backend (CPU always; CUDA when the toolkit is present).
        LlamaNative.EnsureBackendsLoaded();

        // Authoritative check: does the loaded runtime actually expose a non-CPU device?
        // (A CUDA build without its toolkit, or a missing driver, simply does not load.)
        bool gpuDevicePresent = false;
        string? backendName = null;
        try
        {
            if (LlamaNative.LlamaSupportsGpuOffload())
            {
                int count = (int)LlamaNative.GgmlBackendDevCount();
                for (int i = 0; i < count; i++)
                {
                    var dev = LlamaNative.GgmlBackendDevGet((ulong)i);
                    string? name = Marshal.PtrToStringUTF8(LlamaNative.GgmlBackendDevName(dev));
                    if (!string.Equals(name, "CPU", StringComparison.OrdinalIgnoreCase))
                    {
                        gpuDevicePresent = true;
                        backendName = name;
                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Native backend failed to initialize — treat as no usable GPU.
        }

        string note = gpuDevicePresent
            ? $"GPU usable: {gpuName ?? "unknown GPU"}{(vramMb > 0 ? $", {vramMb} MB VRAM" : "")} via {backendName}"
            : gpuName is not null
                ? $"GPU present ({gpuName}{(vramMb > 0 ? $", {vramMb} MB VRAM" : "")}) but no usable GPU backend in this build — CPU fallback"
                : "No GPU detected — CPU only";

        return new HardwareInfo(gpuDevicePresent, gpuName, vramMb, CudaMajorVersion: -1, VulkanVersion: null, note);
    }

    /// <summary>
    /// Recommended <c>GpuLayerCount</c> for the detected hardware. 0 = CPU-only.
    ///
    /// When VRAM and the model size are both known, the whole model plus a ~1 GiB reserve
    /// (KV cache + overhead) must fit; llama.cpp has no CPU spillover, so an undersized
    /// GPU falls back to CPU rather than risk an OOM crash. Unknown values default to
    /// offloading all layers (the <c>999</c> llama.cpp idiom).
    /// </summary>
    public static int RecommendGpuLayerCount(HardwareInfo info, long modelSizeBytes = 0)
    {
        if (!info.GpuUsable)
            return 0;

        const long ReserveBytes = 1L << 30; // ~1 GiB for KV cache + runtime overhead
        if (info.VramMb > 0 && modelSizeBytes > 0)
        {
            long vramBytes = info.VramMb * 1024L * 1024L;
            if (vramBytes < modelSizeBytes + ReserveBytes)
                return 0;
        }

        return 999; // llama.cpp idiom: offload all layers
    }

    /// <summary>
    /// Best-effort GPU name + total VRAM from <c>nvidia-smi</c>. Returns (null, 0) when the
    /// tool is missing or fails — detection degrades to "hardware unknown", never throws.
    /// </summary>
    internal static (string? Name, int VramMb) TryQueryNvidiaSmi()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new()
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,memory.total --format=csv,noheader",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            string line = process.StandardOutput.ReadLine() ?? "";
            process.WaitForExit(5000);

            var parts = line.Split(',');
            if (parts.Length < 2) return (null, 0);

            string name = parts[0].Trim();
            // "16384 MiB" -> 16384
            int vramMb = 0;
            var match = System.Text.RegularExpressions.Regex.Match(parts[1], @"(\d+)\s*MiB");
            if (match.Success)
                int.TryParse(match.Groups[1].Value, out vramMb);

            return (name.Length > 0 ? name : null, vramMb);
        }
        catch (Exception)
        {
            return (null, 0);
        }
    }
}
