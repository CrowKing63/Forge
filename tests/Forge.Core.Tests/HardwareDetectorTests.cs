using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S8 GPU layer recommendation: pure logic over detection facts. No GPU required —
/// the live detection path (nvidia-smi / LLamaSharp SystemInfo) is exercised by the CLI
/// smoke test on this machine, not here.
/// </summary>
public class HardwareDetectorTests
{
    private static HardwareInfo Usable(int vramMb = 16_384) =>
        new(GpuUsable: true, GpuName: "NVIDIA GeForce RTX 5070 Ti", VramMb: vramMb, CudaMajorVersion: 12, VulkanVersion: null, Note: "test");

    [Fact]
    public void NoUsableGpuMeansZeroLayers()
    {
        var noGpu = new HardwareInfo(false, null, 0, -1, null, "no gpu");
        Assert.Equal(0, HardwareDetector.RecommendGpuLayerCount(noGpu, modelSizeBytes: 4_000_000_000));

        // GPU present but the CUDA runtime is unusable (e.g. toolkit missing).
        var broken = new HardwareInfo(false, "NVIDIA GeForce RTX 5070 Ti", 16_384, -1, null, "cpu fallback");
        Assert.Equal(0, HardwareDetector.RecommendGpuLayerCount(broken, modelSizeBytes: 4_000_000_000));
    }

    [Fact]
    public void UsableGpuWithUnknownVramMeansFullOffload()
    {
        var unknown = new HardwareInfo(true, "GPU", VramMb: 0, CudaMajorVersion: 12, VulkanVersion: null, "test");
        Assert.Equal(999, HardwareDetector.RecommendGpuLayerCount(unknown, modelSizeBytes: 4_000_000_000));
    }

    [Fact]
    public void VramTooSmallForModelMeansZeroLayers()
    {
        // 2 GB VRAM cannot hold a 4 GB model + ~1 GiB reserve → CPU.
        var small = new HardwareInfo(true, "GPU", VramMb: 2_048, CudaMajorVersion: 12, VulkanVersion: null, "test");
        Assert.Equal(0, HardwareDetector.RecommendGpuLayerCount(small, modelSizeBytes: 4_000_000_000));
    }

    [Fact]
    public void VramFittingModelMeansFullOffload()
    {
        // 16 GB VRAM comfortably holds a 4 GB model + reserve.
        Assert.Equal(999, HardwareDetector.RecommendGpuLayerCount(Usable(16_384), modelSizeBytes: 4_000_000_000));
    }

    [Fact]
    public void BoundaryIsModelPlusOneGibReserve()
    {
        // Exactly model + 1 GiB: vram == size + reserve fits (strict < is the cutoff).
        long modelBytes = 2L * 1024 * 1024 * 1024; // 2 GiB
        var exact = new HardwareInfo(true, "GPU", VramMb: 3_072, CudaMajorVersion: 12, VulkanVersion: null, "test"); // 3 GiB
        Assert.Equal(999, HardwareDetector.RecommendGpuLayerCount(exact, modelSizeBytes: modelBytes));

        // One byte short of the reserve → CPU.
        var shortByOne = new HardwareInfo(true, "GPU", VramMb: 3_071, CudaMajorVersion: 12, VulkanVersion: null, "test");
        Assert.Equal(0, HardwareDetector.RecommendGpuLayerCount(shortByOne, modelSizeBytes: modelBytes + (1L << 30) + 1));
    }
}
