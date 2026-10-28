using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GameSirBattery
{
    internal struct BatteryReading
    {
        public string Mac;
        public int Percent;
    }

    internal sealed class ScanResult
    {
        public ScanResult()
        {
            Devices = new List<BatteryReading>();
        }

        public List<BatteryReading> Devices { get; private set; }

        public string Error { get; set; }
    }

    /// <summary>
    /// Talks to the GameSir 2.4G receiver over its vendor HID collection
    /// (VID 0x3537, UsagePage 0xFF7A, 65-byte reports).
    /// Response layout: [0]=report id, [1..6]=MAC, [19]=battery percent.
    /// </summary>
    internal static class GameSirDevice
    {
        private const ushort GameSirVendorId = 0x3537;
        private const ushort GameSirReceiverPid = 0x1098;
        private const ushort VendorUsagePage = 0xFF7A;
        private const int ReportSize = 65;
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
        private static readonly Guid HidInterfaceClass = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

        public static ScanResult ReadAll()
        {
            var result = new ScanResult();

            List<string> paths;
            try
            {
                paths = FindVendorCollections();
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }

            if (paths.Count == 0)
            {
                result.Error = "接收器未找到";
                return result;
            }

            foreach (var path in paths)
            {
                string error;
                var reading = TryRead(path, out error);
                if (string.IsNullOrEmpty(reading.Mac))
                {
                    var tag = path.IndexOf("pid_1098", StringComparison.OrdinalIgnoreCase) >= 0 ? "1098" : "other";
                    result.Error = tag + ":" + error;
                    continue;
                }

                var duplicated = result.Devices.Any(d =>
                    string.Equals(d.Mac, reading.Mac, StringComparison.OrdinalIgnoreCase));
                if (!duplicated)
                {
                    result.Devices.Add(reading);
                }
            }

            if (result.Devices.Count == 0 && result.Error == null)
            {
                result.Error = "无法读取电量";
            }

            return result;
        }

        private static BatteryReading TryRead(string path, out string error)
        {
            var reading = new BatteryReading();
            error = null;

            using (var handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                       IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    error = "open " + Marshal.GetLastWin32Error();
                    return reading;
                }

                var request = new byte[ReportSize];
                Array.Copy(DeviceInfoCommand, request, DeviceInfoCommand.Length);

                if (!WriteReport(handle, request, 1000))
                {
                    error = "write " + Marshal.GetLastWin32Error();
                    return reading;
                }

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var response = ReadReport(handle, ReportSize, 1500);
                    if (response == null || response.Length <= 19)
                    {
                        error = "read " + Marshal.GetLastWin32Error();
                        continue;
                    }

                    var payloadStart = response[0] == 0x00 ? 1 : 0;
                    var batteryIndex = payloadStart + 18;
                    if (response.Length <= batteryIndex)
                    {
                        continue;
                    }

                    var percent = response[batteryIndex];
                    if (percent > 100)
                    {
                        continue;
                    }

                    var mac = string.Join(":", response.Skip(payloadStart).Take(6).Select(b => b.ToString("X2")));
                    if (mac == "00:00:00:00:00:00")
                    {
                        continue;
                    }

                    reading.Mac = mac;
                    reading.Percent = percent;
                    return reading;
                }
            }

            return reading;
        }

        private static List<string> FindVendorCollections()
        {
            var found = new List<Tuple<int, string>>();
            var hidClass = HidInterfaceClass;

            var infoSet = SetupDiGetClassDevsW(ref hidClass, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (infoSet == new IntPtr(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                for (uint index = 0; ; index++)
                {
                    var iface = new SpDeviceInterfaceData();
                    iface.cbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData));

                    if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidClass, index, ref iface))
                    {
                        break;
                    }

                    var detail = Marshal.AllocHGlobal(2048);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(infoSet, ref iface, detail, 2048, out _, IntPtr.Zero))
                        {
                            continue;
                        }

                        var path = Marshal.PtrToStringUni(detail + 4);
                        if (string.IsNullOrEmpty(path) ||
                            path.IndexOf("vid_3537", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        using (var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                        {
                            if (handle.IsInvalid)
                            {
                                continue;
                            }

                            var attributes = new HiddAttributes();
                            attributes.Size = (uint)Marshal.SizeOf(typeof(HiddAttributes));
                            if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != GameSirVendorId)
                            {
                                continue;
                            }

                            HidpCaps caps;
                            if (!TryGetCaps(handle, out caps))
                            {
                                continue;
                            }

                            if (caps.UsagePage != VendorUsagePage ||
                                caps.OutputReportByteLength < ReportSize ||
                                caps.InputReportByteLength < ReportSize)
                            {
                                continue;
                            }

                            found.Add(Tuple.Create(attributes.ProductID == GameSirReceiverPid ? 0 : 1, path));
                        }
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

            return found.OrderBy(f => f.Item1).Select(f => f.Item2).ToList();
        }

        private static bool TryGetCaps(SafeFileHandle handle, out HidpCaps caps)
        {
            caps = new HidpCaps();
            caps.Reserved = new ushort[17];

            IntPtr preparsed;
            if (!HidD_GetPreparsedData(handle, out preparsed) || preparsed == IntPtr.Zero)
            {
                return false;
            }

            try
            {
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
                var overlapped = new NativeOverlapped();
                overlapped.EventHandle = ev;

                int written;
                if (WriteFile(handle, buffer, buffer.Length, out written, ref overlapped))
                {
                    return written == buffer.Length;
                }

                if (Marshal.GetLastWin32Error() != ErrorIoPending)
                {
                    return false;
                }

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

        private static byte[] ReadReport(SafeFileHandle handle, int length, int timeoutMs)
        {
            var ev = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
            try
            {
                var buffer = new byte[length];
                var overlapped = new NativeOverlapped();
                overlapped.EventHandle = ev;

                int read;
                if (!ReadFile(handle, buffer, length, out read, ref overlapped))
                {
                    if (Marshal.GetLastWin32Error() != ErrorIoPending)
                    {
                        return null;
                    }

                    if (WaitForSingleObject(ev, timeoutMs) != 0)
                    {
                        CancelIoEx(handle, IntPtr.Zero);
                        return null;
                    }

                    if (!GetOverlappedResult(handle, ref overlapped, out read, false))
                    {
                        return null;
                    }
                }

                if (read <= 0)
                {
                    return null;
                }

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
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

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
}
