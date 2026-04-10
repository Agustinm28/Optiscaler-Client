using System;
using OptiscalerClient.Services;

namespace OptiscalerClient.Helpers
{
    public static class GpuSelectionHelper
    {
        public static string BuildGpuId(GpuInfo gpu)
        {
            return $"{gpu.Vendor}|{gpu.Name}";
        }

        public static GpuInfo? GetPreferredGpu(IGpuDetectionService? gpuService, string? defaultGpuId)
        {
            if (gpuService == null) return null;

            var gpus = gpuService.DetectGPUs();
            if (gpus.Length == 0) return null;

            if (!string.IsNullOrWhiteSpace(defaultGpuId))
            {
                foreach (var gpu in gpus)
                {
                    if (string.Equals(BuildGpuId(gpu), defaultGpuId, StringComparison.OrdinalIgnoreCase))
                    {
                        return gpu;
                    }
                }
            }

            return gpuService.GetDiscreteGPU() ?? gpuService.GetPrimaryGPU() ?? gpus[0];
        }
    }
}
