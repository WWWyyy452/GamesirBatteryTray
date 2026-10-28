"""Stage 3: replay the GameSir vendor command and dump the device-info response.

The official client logs show:
    hid_write (65) 00010100
    hid_read  (64) 01 f4 96 50 03 25 01 00 00 00 28 00 00 00 00 00 04 02 0c 00 ...
which the renderer decodes into { mac: 01f496500325, batteryLevel: 12, ... }

Usage: python probe3_battery.py
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
kernel32.WriteFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(OVERLAPPED)]
kernel32.CancelIoEx.argtypes = [ctypes.c_void_p, ctypes.POINTER(OVERLAPPED)]
kernel32.GetOverlappedResult.argtypes = [ctypes.c_void_p, ctypes.POINTER(OVERLAPPED), ctypes.POINTER(wintypes.DWORD), wintypes.BOOL]

hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]

CMD_DEVICE_INFO = bytes([0x00, 0x01, 0x01, 0x00])


def find_vendor_paths():
    guid = GUID.from_buffer_copy(GUID_DEVINTERFACE_HID)
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    out = []
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
            h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                                     FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
            if not h or h == INVALID_HANDLE_VALUE:
                continue
            try:
                attrs = HIDD_ATTRIBUTES()
                attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
                hid.HidD_GetAttributes(h, ctypes.byref(attrs))
                caps = HIDP_CAPS()
                pp = ctypes.c_void_p()
                ok = bool(hid.HidD_GetPreparsedData(h, ctypes.byref(pp))) and bool(hid.HidP_GetCaps(pp, ctypes.byref(caps)))
                if pp:
                    hid.HidD_FreePreparsedData(pp)
                if ok and attrs.VendorID == 0x3537 and caps.UsagePage >= 0xFF00 and caps.OutputReportByteLength >= 65:
                    out.append((path, attrs.ProductID, caps))
            finally:
                kernel32.CloseHandle(h)
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return out


def transact(path, out_len, in_len, payload):
    h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING,
                             FILE_FLAG_OVERLAPPED, None)
    if not h or h == INVALID_HANDLE_VALUE:
        return "open failed (err %d)" % ctypes.get_last_error()

    ev = kernel32.CreateEventW(None, False, False, None)
    results = []
    try:
        buf = ctypes.create_string_buffer(out_len)
        ctypes.memmove(buf, payload, len(payload))
        ov = OVERLAPPED()
        ov.hEvent = ev
        written = wintypes.DWORD(0)
        if not kernel32.WriteFile(h, buf, out_len, ctypes.byref(written), ctypes.byref(ov)):
            err = ctypes.get_last_error()
            if err != 997:
                return "write failed (err %d)" % err
            if kernel32.WaitForSingleObject(ev, 2000) != WAIT_OBJECT_0:
                kernel32.CancelIoEx(h, ctypes.byref(ov))
                return "write timeout"
            if not kernel32.GetOverlappedResult(h, ctypes.byref(ov), ctypes.byref(written), False):
                return "write result failed (err %d)" % ctypes.get_last_error()
        if written.value != out_len:
            results.append("write returned %d/%d bytes" % (written.value, out_len))

        for _ in range(3):
            rbuf = ctypes.create_string_buffer(in_len)
            ov2 = OVERLAPPED()
            ov2.hEvent = ev
            read = wintypes.DWORD(0)
            ok = kernel32.ReadFile(h, rbuf, in_len, ctypes.byref(read), ctypes.byref(ov2))
            if not ok:
                err = ctypes.get_last_error()
                if err != 997:
                    results.append("read failed (err %d)" % err)
                    break
                if kernel32.WaitForSingleObject(ev, 1500) != WAIT_OBJECT_0:
                    kernel32.CancelIoEx(h, ctypes.byref(ov2))
                    break
                if not kernel32.GetOverlappedResult(h, ctypes.byref(ov2), ctypes.byref(read), False):
                    results.append("read result failed (err %d)" % ctypes.get_last_error())
                    break
            data = rbuf.raw[:read.value]
            if data:
                results.append(data.hex(" "))
                if len(data) > 18 and data[0] != 0xF4:
                    break
    finally:
        kernel32.CloseHandle(ev)
        kernel32.CloseHandle(h)
    return " | ".join(results) if results else "no response"


def main():
    targets = find_vendor_paths()
    if not targets:
        print("no GameSir vendor collection found (is the controller connected?)")
        return 1

    for path, pid, caps in targets:
        print("\n=== PID %04X  up=0x%02X ===" % (pid, caps.UsagePage))
        print("path:", path)
        for attempt in range(3):
            resp = transact(path, caps.OutputReportByteLength, caps.InputReportByteLength, CMD_DEVICE_INFO)
            print("  [%d] device-info -> %s" % (attempt + 1, resp))
    return 0


if __name__ == "__main__":
    sys.exit(main())
