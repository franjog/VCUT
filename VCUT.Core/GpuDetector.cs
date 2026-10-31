using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VCUT.Core;

public sealed class GpuInfo
{
    public GpuInfo(string name, string deviceName, bool isPrimary, long dedicatedVideoMemoryMb = 0, string deviceId = "")
    {
        Name = (name ?? string.Empty).Trim();
        DeviceName = (deviceName ?? string.Empty).Trim();
        IsPrimary = isPrimary;
        DedicatedVideoMemoryMb = Math.Max(0L, dedicatedVideoMemoryMb);
        Vendor = GpuDetector.DetectVendor(Name, deviceId);
        PreferredAccelerator = GpuDetector.GetPreferredAccelerator(Name, deviceId);
        IsDiscrete = GpuDetector.IsDiscreteAdapter(Name, DedicatedVideoMemoryMb, deviceId);
        ShortName = GpuDetector.FormatShortName(Name);
    }

    public string Name { get; }
    public string ShortName { get; }
    public string DeviceName { get; }
    public bool IsPrimary { get; }
    public string Vendor { get; }
    public long DedicatedVideoMemoryMb { get; }
    public string PreferredAccelerator { get; }
    public bool IsDiscrete { get; }
}

public static class GpuDetector
{
    private const int AttachedToDesktop = 0x1;
    private const int PrimaryDevice = 0x4;
    private const int MirroringDriver = 0x8;
    private const int RemoteDevice = 0x4000000;
    private const int Disconnect = 0x2000000;
    private const string DisplayClassRegistryPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string DirectXUserGpuPreferencesPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplayDevices(string device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);

    public static IReadOnlyList<GpuInfo> Detect()
    {
        var candidates = new List<GpuInfo>();
        if (!OperatingSystem.IsWindows())
        {
            return candidates;
        }

        var primaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayDevices = new List<GpuInfo>();
        uint index = 0;
        while (true)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            bool available = (device.StateFlags & (AttachedToDesktop | PrimaryDevice)) != 0;
            bool excluded = (device.StateFlags & (MirroringDriver | RemoteDevice | Disconnect)) != 0;
            if (available && !excluded && IsHardwareAdapterName(device.DeviceString))
            {
                bool isPrimary = (device.StateFlags & PrimaryDevice) != 0;
                string cleanName = device.DeviceString.Trim();
                if (isPrimary)
                {
                    primaryNames.Add(cleanName);
                }

                displayDevices.Add(new GpuInfo(cleanName, device.DeviceName.Trim(), isPrimary, 0, device.DeviceId));
            }

            index++;
        }

        try
        {
            using RegistryKey classKey = Registry.LocalMachine.OpenSubKey(DisplayClassRegistryPath, writable: false);
            if (classKey is not null)
            {
                foreach (string subKeyName in classKey.GetSubKeyNames())
                {
                    if (!subKeyName.StartsWith("0", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using RegistryKey adapterKey = classKey.OpenSubKey(subKeyName, writable: false);
                    if (adapterKey is null)
                    {
                        continue;
                    }

                    string driverDesc = adapterKey.GetValue("DriverDesc") as string;
                    if (!IsHardwareAdapterName(driverDesc))
                    {
                        continue;
                    }

                    string matchingId = adapterKey.GetValue("MatchingDeviceId") as string ?? string.Empty;
                    long vramBytes = ReadRegistryMemoryBytes(adapterKey);
                    long vramMb = vramBytes / (1024L * 1024L);
                    string cleanName = driverDesc.Trim();
                    bool isPrimary = primaryNames.Contains(cleanName);
                    candidates.Add(new GpuInfo(cleanName, subKeyName, isPrimary, vramMb, matchingId));
                }
            }
        }
        catch
        {
        }

        candidates.AddRange(displayDevices);

        return candidates
            .GroupBy(gpu => gpu.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(g => g.DedicatedVideoMemoryMb)
                .ThenByDescending(g => g.IsPrimary)
                .First())
            .OrderByDescending(gpu => gpu.IsDiscrete)
            .ThenByDescending(gpu => gpu.DedicatedVideoMemoryMb)
            .ThenByDescending(gpu => gpu.IsPrimary)
            .ToList();
    }

    public static bool ConfigureHighPerformanceGpuPreference(string executablePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(DirectXUserGpuPreferencesPath, writable: true);
            if (key is null)
            {
                return false;
            }

            string existing = key.GetValue(fullPath) as string;
            if (string.IsNullOrWhiteSpace(existing) || !existing.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(fullPath, "GpuPreference=2;", RegistryValueKind.String);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsHardwareAdapterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string normalized = name.Trim();
        return !normalized.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("Indirect Display", StringComparison.OrdinalIgnoreCase);
    }

    public static string DetectVendor(string adapterName, string deviceId = "")
    {
        string combined = ((adapterName ?? string.Empty) + " " + (deviceId ?? string.Empty)).ToUpperInvariant();
        if (combined.Contains("NVIDIA") || combined.Contains("GEFORCE") || combined.Contains("RTX") || combined.Contains("GTX") || combined.Contains("QUADRO") || combined.Contains("VEN_10DE"))
        {
            return "NVIDIA";
        }

        if (combined.Contains("AMD") || combined.Contains("RADEON") || combined.Contains("VEN_1002"))
        {
            return "AMD";
        }

        if (combined.Contains("INTEL") || combined.Contains("ARC") || combined.Contains("IRIS") || combined.Contains("UHD") || combined.Contains("VEN_8086"))
        {
            return "Intel";
        }

        return "Direct3D11";
    }

    public static string GetPreferredAccelerator(string adapterName, string deviceId = "")
    {
        return DetectVendor(adapterName, deviceId) switch
        {
            "NVIDIA" => "cuda",
            "AMD" => "amf",
            "Intel" => "qsv",
            _ => "d3d11va"
        };
    }

    public static bool IsDiscreteAdapter(string adapterName, long dedicatedMemoryMb, string deviceId = "")
    {
        string vendor = DetectVendor(adapterName, deviceId);
        if (vendor == "NVIDIA")
        {
            return true;
        }

        string upper = (adapterName ?? string.Empty).ToUpperInvariant();
        if (vendor == "AMD" && (upper.Contains("RX ") || upper.Contains("PRO ") || dedicatedMemoryMb >= 2048))
        {
            return true;
        }

        if (vendor == "Intel" && upper.Contains("ARC") && dedicatedMemoryMb >= 2048)
        {
            return true;
        }

        return dedicatedMemoryMb >= 4096 && !upper.Contains("INTEL(R) GRAPHICS") && !upper.Contains("UHD GRAPHICS") && !upper.Contains("IRIS");
    }

    public static string FormatShortName(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return "GPU";
        }

        string clean = adapterName.Trim()
            .Replace("NVIDIA GeForce ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD Radeon ", "Radeon ", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel(R) ", "Intel ", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Laptop GPU", "Laptop", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return clean.Length > 24 ? clean.Substring(0, 24).Trim() : clean;
    }

    private static long ReadRegistryMemoryBytes(RegistryKey adapterKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0L;
        }

        object qwValue = adapterKey.GetValue("HardwareInformation.qwMemorySize");
        if (qwValue is long lVal)
        {
            return Math.Max(0L, lVal);
        }

        if (qwValue is byte[] qwBytes && qwBytes.Length >= 8)
        {
            return Math.Max(0L, BitConverter.ToInt64(qwBytes, 0));
        }

        object dwValue = adapterKey.GetValue("HardwareInformation.MemorySize");
        if (dwValue is int iVal)
        {
            return Math.Max(0L, (long)(uint)iVal);
        }

        if (dwValue is byte[] dwBytes && dwBytes.Length >= 4)
        {
            return Math.Max(0L, (long)BitConverter.ToUInt32(dwBytes, 0));
        }

        return 0L;
    }
}

