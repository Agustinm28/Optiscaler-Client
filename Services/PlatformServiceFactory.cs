// OptiScaler Client - A frontend for managing OptiScaler installations
// Copyright (C) 2026 Agustín Montaña (Agustinm28)
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services;

/// <summary>
/// Central factory for platform-specific service implementations.
/// Callers obtain the correct implementation without scattering
/// <c>OperatingSystem.IsWindows()</c> guards throughout the codebase.
/// </summary>
public static class PlatformServiceFactory
{
    /// <summary>Returns the <see cref="IShellService"/> for the current OS.</summary>
    public static IShellService CreateShellService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsShellService();
        return new XdgShellService();
    }

    /// <summary>Returns the <see cref="IGpuDetectionService"/> for the current OS,
    /// or <c>null</c> on unsupported platforms.</summary>
    public static IGpuDetectionService? CreateGpuDetectionService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsGpuDetectionService();
        if (OperatingSystem.IsLinux())
            return new LinuxGpuDetectionService();
        return null;
    }

    // ── Private implementations ────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private sealed class WindowsShellService : IShellService
    {
        public void OpenFolder(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }

        public void OpenUrl(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private sealed class XdgShellService : IShellService
    {
        private static readonly string[] _candidates = ["xdg-open", "/usr/bin/xdg-open", "open"];

        public void OpenFolder(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            LaunchXdg(path);
        }

        public void OpenUrl(string url) => LaunchXdg(url);

        private static void LaunchXdg(string target)
        {
            foreach (var exe in _candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                    psi.ArgumentList.Add(target);
                    Process.Start(psi);
                    return;
                }
                catch (System.ComponentModel.Win32Exception) { /* try next */ }
            }
            throw new System.InvalidOperationException(
                $"Could not find a suitable program to open '{target}'. " +
                $"Tried: {string.Join(", ", _candidates)}");
        }
    }
}
using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services;

/// <summary>
/// Creates the correct platform-specific service implementations at startup.
/// Centralizes all OperatingSystem.Is* checks so individual views and services
/// don't need to repeat them.
/// </summary>
public static class PlatformServiceFactory
{
    /// <summary>
    /// Returns the appropriate GPU detection service for the current OS.
    /// On unsupported platforms returns a no-op implementation that degrades
    /// gracefully rather than throwing.
    /// </summary>
    public static IGpuDetectionService CreateGpuDetectionService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsGpuDetectionService();
        if (OperatingSystem.IsLinux())
            return new LinuxGpuDetectionService();
        return new NullGpuDetectionService();
    }

    /// <summary>
    /// Returns the appropriate shell service for the current OS.
    /// Falls back to xdg-open on any non-Windows platform.
    /// </summary>
    public static IShellService CreateShellService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsShellService();
        return new XdgShellService();
    }
}

// ── Windows shell ────────────────────────────────────────────────────────────

[SupportedOSPlatform("windows")]
internal sealed class WindowsShellService : IShellService
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}

// ── Linux / XDG shell ────────────────────────────────────────────────────────

internal sealed class XdgShellService : IShellService
{
    private static readonly string[] _bins = ["xdg-open", "/usr/bin/xdg-open", "open"];

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        TryOpen(path);
    }

    public void OpenUrl(string url) => TryOpen(url);

    private static void TryOpen(string target)
    {
        foreach (var bin in _bins)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = bin,
                    ArgumentList = { target },
                    UseShellExecute = false
                });
                return;
            }
            catch { }
        }
    }
}

// ── No-op fallback for unsupported platforms ─────────────────────────────────

/// <summary>
/// Used when no GPU detection is available for the current platform.
/// Returns empty/null values instead of throwing.
/// </summary>
internal sealed class NullGpuDetectionService : IGpuDetectionService
{
    public GpuInfo[] DetectGPUs() => Array.Empty<GpuInfo>();
    public GpuInfo? GetPrimaryGPU() => null;
    public GpuInfo? GetDiscreteGPU() => null;
    public bool HasGPU(GpuVendor vendor) => false;
    public string GetGPUDescription() => "GPU detection not supported on this platform";
}
