namespace OptiscalerClient.Services
{
    public interface IGpuDetectionService
    {
        GpuInfo[] DetectGPUs();
        GpuInfo? GetPrimaryGPU();
        GpuInfo? GetDiscreteGPU();
        bool HasGPU(GpuVendor vendor);
        string GetGPUDescription();
    }
}
