using LLama.Native;

namespace Forge.Core;

/// <summary>
/// Snapshot of the machine's inference hardware, as seen by Forge at startup.
/// </summary>
/// <param name="GpuUsable">True when a GPU backend is usable for layer offload in this build.</param>
/// <param name="GpuName">GPU display name (e.g. "NVIDIA GeForce RTX 5070 Ti"); null when unknown.</param>
/// <param name="VramMb">Total VRAM in MB; 0 when unknown.</param>
/// <param name="CudaMajorVersion">CUDA toolkit major version detected by LLamaSharp; -1 when absent.</param>
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
/// LLamaSharp locks its native runtime selection on the first native call in a process, so
/// <see cref="Detect"/> must run before any model load (the app calls it once at startup).
/// </summary>
public static class HardwareDetector
{
    /// <summary>
    /// Probes the machine: OS-level GPU name + VRAM (nvidia-smi), CUDA/Vulkan availability
    /// (LLamaSharp <see cref="SystemInfo"/>), and — authoritatively — whether the loaded
    /// runtime exposes a non-CPU device. Never throws.
    /// </summary>
    public static HardwareInfo Detect()
    {
        var (gpuName, vramMb) = TryQueryNvidiaSmi();

        // Configure the probe intent BEFORE any native call: LLamaSharp locks the runtime
        // selection when the first native library loads. If detection runs after a load
        // (e.g. the settings window), the selection is already fixed and only re-queried.
        // Prefer CUDA, then Vulkan; the CPU build stays as the fallback so a machine with no
        // usable GPU backend keeps a working runtime for later CPU loads.
        if (!NativeLibraryConfig.LLama.LibraryHasLoaded)
            NativeLibraryConfig.All.WithCuda(true).WithVulkan(true);

        int cudaMajor = -1;
        string? vulkanVersion = null;
        try
        {
            var sys = SystemInfo.Get();
            cudaMajor = sys.CudaMajorVersion;
            vulkanVersion = sys.VulkanVersion;
        }
        catch (Exception)
        {
            // Unsupported platform or native failure — treat as no CUDA.
        }

        // Authoritative check: does the loaded runtime actually expose a non-CPU device?
        // (A CUDA build without its toolkit, or a Vulkan build without a driver, fails to
        // load and falls back to the CPU build, which reports no offload support.)
        bool gpuDevicePresent = false;
        try
        {
            if (NativeApi.llama_supports_gpu_offload())
            {
                int count = (int)NativeApi.ggml_backend_dev_count();
                for (int i = 0; i < count; i++)
                {
                    var dev = NativeApi.ggml_backend_dev_get((UIntPtr)i);
                    string? name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(
                        NativeApi.ggml_backend_dev_name(dev));
                    if (!string.Equals(name, "CPU", StringComparison.OrdinalIgnoreCase))
                    {
                        gpuDevicePresent = true;
                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Native backend failed to initialize — treat as no usable GPU.
        }

        string? backend = null;
        if (gpuDevicePresent)
        {
            try
            {
                // Reports which backend LLamaSharp selected (informational for the note).
                if (NativeLibraryConfig.All.DryRun(out var lib, out _))
                    backend = lib?.Metadata?.UseCuda == true ? "CUDA"
                        : lib?.Metadata?.UseVulkan == true ? "Vulkan"
                        : null;
            }
            catch (Exception) { }
        }

        string note = gpuDevicePresent
            ? $"GPU usable: {gpuName ?? "unknown GPU"}{(vramMb > 0 ? $" ({vramMb} MB VRAM)" : "")} via {backend ?? "GPU backend"}"
            : gpuName is not null
                ? $"GPU present ({gpuName}{(vramMb > 0 ? $", {vramMb} MB VRAM" : "")}) but no usable GPU backend in this build — CPU fallback"
                : "No GPU detected — CPU only";

        return new HardwareInfo(gpuDevicePresent, gpuName, vramMb, cudaMajor, vulkanVersion, note);
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
