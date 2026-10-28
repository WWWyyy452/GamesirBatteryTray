using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GamesirBatteryTray;

/// <summary>Result of one battery query.</summary>
internal readonly record struct BatteryReading(bool Connected, int? Percent, string? Mac, string? Error)
{
    public static BatteryReading Offline(string? reason) => new(false, null, null, reason);
}

/// <summary>
/// Talks to the GameSir 2.4G receiver over its vendor-specific HID collection
/// (UsagePage 0xFF7A, 65-byte reports, VID 0x3537).
///
/// The official client sends "00 01 01 00" (device info) and decodes the answer as
///   [0]=report id, [1..6]=MAC, [10..11]=dongle version, [19]=battery percent.
/// </summary>
internal static class GameSirDevice
{
    private const ushort GameSirVendorId = 0x3537;
    private const ushort GameSirReceiverPid = 0x1098;
    private const ushort VendorUsagePage = 0xFF7A;
    private const int ReportSize = 65;
    private const int BatteryIndexInReport = 19; // 0-based, including the leading report id
    private const int ErrorIoPending = 997;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    private static readonly byte[] DeviceInfoCommand = { 0x00, 0x01, 0x01, 0x00 };
    private static readonly Guid HidInterfaceClass = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    /// <summary>Queries every connected receiver; one entry per unique MAC address.</summary>
    public static (IReadOnlyList<BatteryReading> Devices, string? Error) ReadAll()
    {
        List<string> paths;
        try
        {
            paths = FindVendorCollections();
        }
        catch (Exception ex)
        {
            return (Array.Empty<BatteryReading>(), ex.Message);
        }

        if (paths.Count == 0)
            return (Array.Empty<BatteryReading>(), "接收器未找到");

        var devices = new List<BatteryReading>();
        string? lastError = null;

        foreach (var path in paths)
        {
            var reading = TryRead(path);
            if (!reading.Connected)
            {
                lastError = reading.Error;
                continue;
            }

            var duplicate = devices.Any(d => string.Equals(d.Mac, reading.Mac, StringComparison.OrdinalIgnoreCase));
            if (!duplicate) devices.Add(reading);
        }

        if (devices.Count == 0)
            return (devices, lastError ?? "无法读取电量");

        return (devices, null);
    }

    private static BatteryReading TryRead(string path)
    {
        using var handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            return BatteryReading.Offline("打开设备失败");

        var request = new byte[ReportSize];
        Array.Copy(DeviceInfoCommand, request, DeviceInfoCommand.Length);

        if (!WriteReport(handle, request, 1000))
            return BatteryReading.Offline("写入失败");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = ReadReport(handle, ReportSize, 1500);
            if (response is null || response.Length <= BatteryIndexInReport) continue;

            // Strip the report id (0x00) that HID prepends to the payload.
            var payloadStart = response[0] == 0x00 ? 1 : 0;
            var batteryIndex = payloadStart + 18;
            if (response.Length <= batteryIndex) continue;

            var percent = response[batteryIndex];
            if (percent > 100) continue;

            var mac = string.Join(":", response.Skip(payloadStart).Take(6).Select(b => b.ToString("X2")));
            if (mac == "00:00:00:00:00:00") continue; // stale / not really listening

            return new BatteryReading(true, percent, mac, null);
        }

        return BatteryReading.Offline("设备无响应");
    }

    private static List<string> FindVendorCollections()
    {
        var found = new List<(int Priority, string Path)>();
        var hidClass = HidInterfaceClass;

        var infoSet = SetupDiGetClassDevsW(ref hidClass, null, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (infoSet == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidClass, index, ref iface))
                    break;

                var detail = Marshal.AllocHGlobal(2048);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(infoSet, ref iface, detail, 2048, out _, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (string.IsNullOrEmpty(path) ||
                        !path.Contains("vid_3537", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;

                    var attributes = new HiddAttributes { Size = (uint)Marshal.SizeOf<HiddAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes)) continue;
                    if (attributes.VendorID != GameSirVendorId) continue;

                    if (!TryGetCaps(handle, out var caps)) continue;
                    if (caps.UsagePage != VendorUsagePage) continue;
                    if (caps.OutputReportByteLength < ReportSize || caps.InputReportByteLength < ReportSize)
                        continue;

                    found.Add((attributes.ProductID == GameSirReceiverPid ? 0 : 1, path));
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }

        return found.OrderBy(f => f.Priority).Select(f => f.Path).ToList();
    }

    private static bool TryGetCaps(SafeFileHandle handle, out HidpCaps caps)
    {
        caps = default;
        if (!HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
            return false;

        try
        {
            caps = new HidpCaps { Reserved = new ushort[17] };
            return HidP_GetCaps(preparsed, ref caps);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static bool WriteReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        var ev = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
        try
        {
            var overlapped = new NativeOverlapped { EventHandle = ev };
            if (WriteFile(handle, buffer, buffer.Length, out var written, ref overlapped))
                return written == buffer.Length;

            if (Marshal.GetLastWin32Error() != ErrorIoPending) return false;
            if (WaitForSingleObject(ev, timeoutMs) != 0)
            {
                CancelIoEx(handle, IntPtr.Zero);
                return false;
            }

            return GetOverlappedResult(handle, ref overlapped, out written, false) && written == buffer.Length;
        }
        finally
        {
            CloseHandle(ev);
        }
    }

    private static byte[]? ReadReport(SafeFileHandle handle, int length, int timeoutMs)
    {
        var ev = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
        try
        {
            var buffer = new byte[length];
            var overlapped = new NativeOverlapped { EventHandle = ev };

            if (!ReadFile(handle, buffer, length, out var read, ref overlapped))
            {
                if (Marshal.GetLastWin32Error() != ErrorIoPending) return null;
                if (WaitForSingleObject(ev, timeoutMs) != 0)
                {
                    CancelIoEx(handle, IntPtr.Zero);
                    return null;
                }
                if (!GetOverlappedResult(handle, ref overlapped, out read, false)) return null;
            }

            if (read <= 0) return null;
            var result = new byte[read];
            Array.Copy(buffer, result, read);
            return result;
        }
        finally
        {
            CloseHandle(ev);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern bool HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int bytesToWrite,
        out int bytesWritten, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int bytesToRead,
        out int bytesRead, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, ref NativeOverlapped overlapped,
        out int bytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr eventAttributes, bool manualReset, bool initialState,
        IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
