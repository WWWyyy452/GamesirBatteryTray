"""Stage 2: inspect the vendor-specific HID channel of the GameSir 2.4G receiver.

Lists every HID interface, then passively listens on vendor-defined collections for a
few seconds and prints whatever reports the device pushes (unprompted), plus a few
HidD_GetInputReport attempts.

Usage: python probe2_vendor.py [seconds]
"""

import ctypes
import sys
import uuid
from ctypes import wintypes

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x1
FILE_SHARE_WRITE = 0x2
OPEN_EXISTING = 3
FILE_FLAG_OVERLAPPED = 0x40000000
DIGCF_PRESENT = 0x2
DIGCF_DEVICEINTERFACE = 0x10
IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0191
WAIT_OBJECT_0 = 0
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
GUID_DEVINTERFACE_HID = uuid.UUID("4d1e55b2-f16f-11cf-88cb-001111000030").bytes_le

setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD),
                ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", wintypes.DWORD), ("Reserved", ctypes.c_size_t)]


class SP_DETAIL_W(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("DevicePath", ctypes.c_wchar * 1024)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", wintypes.ULONG), ("VendorID", wintypes.USHORT),
                ("ProductID", wintypes.USHORT), ("VersionNumber", wintypes.USHORT)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [
        ("Usage", wintypes.USHORT), ("UsagePage", wintypes.USHORT),
        ("InputReportByteLength", wintypes.USHORT),
        ("OutputReportByteLength", wintypes.USHORT),
        ("FeatureReportByteLength", wintypes.USHORT),
        ("Reserved", wintypes.USHORT * 17),
        ("NumberLinkCollectionNodes", wintypes.USHORT),
        ("NumberInputButtonCaps", wintypes.USHORT),
        ("NumberInputValueCaps", wintypes.USHORT),
        ("NumberInputDataIndices", wintypes.USHORT),
        ("NumberOutputButtonCaps", wintypes.USHORT),
        ("NumberOutputValueCaps", wintypes.USHORT),
        ("NumberOutputDataIndices", wintypes.USHORT),
        ("NumberFeatureButtonCaps", wintypes.USHORT),
        ("NumberFeatureValueCaps", wintypes.USHORT),
        ("NumberFeatureDataIndices", wintypes.USHORT),
    ]


class OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_size_t), ("InternalHigh", ctypes.c_size_t),
                ("Offset", wintypes.DWORD), ("OffsetHigh", wintypes.DWORD),
                ("hEvent", ctypes.c_void_p)]


setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wintypes.LPCWSTR, wintypes.HWND, wintypes.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(GUID), wintypes.DWORD, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [ctypes.c_void_p, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA), ctypes.POINTER(SP_DETAIL_W), wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [ctypes.c_void_p]

kernel32.CreateFileW.restype = ctypes.c_void_p
kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
kernel32.CreateEventW.restype = ctypes.c_void_p
kernel32.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.WaitForSingleObject.argtypes = [ctypes.c_void_p, wintypes.DWORD]
kernel32.ReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(OVERLAPPED)]
kernel32.CancelIoEx.argtypes = [ctypes.c_void_p, ctypes.POINTER(OVERLAPPED)]
kernel32.DeviceIoControl.argtypes = [ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]

hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetProductString.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_GetInputReport.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]


def open_hid(path, overlapped=False):
    h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                             OPEN_EXISTING, FILE_FLAG_OVERLAPPED if overlapped else 0, None)
    return None if (not h or h == INVALID_HANDLE_VALUE) else h


def device_info(h):
    attrs = HIDD_ATTRIBUTES()
    attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
    hid.HidD_GetAttributes(h, ctypes.byref(attrs))
    buf = ctypes.create_unicode_buffer(256)
    product = buf.value if hid.HidD_GetProductString(h, buf, ctypes.sizeof(buf)) else ""
    caps = HIDP_CAPS()
    pp = ctypes.c_void_p()
    ok = bool(hid.HidD_GetPreparsedData(h, ctypes.byref(pp))) and bool(hid.HidP_GetCaps(pp, ctypes.byref(caps)))
    if pp:
        hid.HidD_FreePreparsedData(pp)
    return attrs, product, caps if ok else None


def read_descriptor(h):
    buf = ctypes.create_string_buffer(4096)
    n = wintypes.DWORD(0)
    if kernel32.DeviceIoControl(h, IOCTL_HID_GET_REPORT_DESCRIPTOR, None, 0,
                                buf, ctypes.sizeof(buf), ctypes.byref(n), None) and n.value:
        return buf.raw[:n.value]
    return b""


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 6.0
    guid = GUID.from_buffer_copy(GUID_DEVINTERFACE_HID)
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if not hdev or hdev == -1:
        print("enumeration failed")
        return 1

    targets = []
    idx = 0
    try:
        while True:
            iface = SP_DEVICE_INTERFACE_DATA()
            iface.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
            if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid), idx, ctypes.byref(iface)):
                break
            idx += 1
            detail = SP_DETAIL_W()
            detail.cbSize = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
            need = wintypes.DWORD(0)
            if not setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(iface), ctypes.byref(detail),
                                                            ctypes.sizeof(detail), ctypes.byref(need), None):
                continue
            path = detail.DevicePath
            h = open_hid(path)
            if not h:
                continue
            try:
                attrs, product, caps = device_info(h)
                if not caps:
                    continue
                print("VID:%04X PID:%04X  up=0x%02X u=0x%02X  in=%3d out=%3d feat=%3d  %-32s %s" % (
                    attrs.VendorID, attrs.ProductID, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength,
                    caps.FeatureReportByteLength, product or "-",
                    path.split("#")[1] if "#" in path else path))
                if attrs.VendorID == 0x3537:
                    targets.append((path, attrs, product, caps))
            finally:
                kernel32.CloseHandle(h)
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hdev)

    print("\n" + "=" * 78)
    print(" Passive listen on GameSir vendor collections (%g s each)" % seconds)
    print("=" * 78)

    for path, attrs, product, caps in targets:
        if caps.UsagePage < 0xFF00:
            continue
        print("\n--- 0x%04X collection on PID %04X (%s) ---" % (caps.UsagePage, attrs.ProductID, product))
        print("  descriptor: %s" % read_descriptor(open_hid(path)).hex(" "))
        h = open_hid(path, overlapped=True)
        if not h:
            print("  cannot open")
            continue
        try:
            if caps.InputReportByteLength:
                length = caps.InputReportByteLength
                seen = 0
                deadline_hits = 0
                while deadline_hits < max(2, int(seconds / 1.0)):
                    buf = ctypes.create_string_buffer(length)
                    ov = OVERLAPPED()
                    ov.hEvent = kernel32.CreateEventW(None, True, False, None)
                    read = wintypes.DWORD(0)
                    kernel32.ReadFile(h, buf, length, ctypes.byref(read), ctypes.byref(ov))
                    rc = kernel32.WaitForSingleObject(ov.hEvent, 1000)
                    if rc == WAIT_OBJECT_0 and read.value:
                        print("  input: %s" % buf.raw[:read.value].hex(" "))
                        seen += 1
                    else:
                        kernel32.CancelIoEx(h, ctypes.byref(ov))
                        deadline_hits += 1
                    kernel32.CloseHandle(ov.hEvent)
                if seen == 0:
                    print("  (no unprompted input reports)")

            print("  HidD_GetInputReport attempts:")
            for rid in range(0, 4):
                buf = ctypes.create_string_buffer(max(caps.InputReportByteLength, 64))
                buf[0] = bytes([rid])
                if hid.HidD_GetInputReport(h, buf, ctypes.sizeof(buf)):
                    print("    id 0x%02X -> %s" % (rid, buf.raw[:min(64, ctypes.sizeof(buf))].hex(" ")))
                else:
                    print("    id 0x%02X -> failed (err %d)" % (rid, ctypes.get_last_error()))
        finally:
            kernel32.CloseHandle(h)

    print("\nDone.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
