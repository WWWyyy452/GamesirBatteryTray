"""Probe a 2.4G game controller (e.g. GameSir Nova 2) for battery information.

Usage:
    python probe_gamepad.py

Plug in the 2.4G receiver, turn the controller on, then run this.
No third-party packages are required (pure ctypes against setupapi / hid / xinput).

What it reports:
  1. Every HID interface that looks like a game controller (VID/PID, usage page/usage,
     feature report length, raw HID report descriptor).
  2. Whether the HID report descriptor declares the standard
     "Battery Strength" usage (Usage Page 0x06, Usage 0x20).
  3. Feature reports 0x00..0x0F read back from the device (private/vendor channels).
  4. XInput slot status + XInputGetBatteryInformation result for slots 0..3.
"""

import ctypes
import sys
import uuid
from ctypes import wintypes

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3
DIGCF_PRESENT = 0x00000002
DIGCF_DEVICEINTERFACE = 0x00000010
IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0191
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

# Windows "HID class" device interface class GUID
GUID_DEVINTERFACE_HID = uuid.UUID("4d1e55b2-f16f-11cf-88cb-001111000030").bytes_le

setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)


class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", wintypes.DWORD),
        ("Data2", wintypes.WORD),
        ("Data3", wintypes.WORD),
        ("Data4", ctypes.c_ubyte * 8),
    ]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("InterfaceClassGuid", GUID),
        ("Flags", wintypes.DWORD),
        ("Reserved", ctypes.c_size_t),
    ]


class SP_DEVICE_INTERFACE_DETAIL_DATA_W(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("DevicePath", ctypes.c_wchar * 1024),
    ]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [
        ("Size", wintypes.ULONG),
        ("VendorID", wintypes.USHORT),
        ("ProductID", wintypes.USHORT),
        ("VersionNumber", wintypes.USHORT),
    ]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [
        ("Usage", wintypes.USHORT),
        ("UsagePage", wintypes.USHORT),
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


setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wintypes.LPCWSTR, wintypes.HWND, wintypes.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(GUID), wintypes.DWORD, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [ctypes.c_void_p, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA), ctypes.POINTER(SP_DEVICE_INTERFACE_DETAIL_DATA_W), wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [ctypes.c_void_p]

kernel32.CreateFileW.restype = ctypes.c_void_p
kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
kernel32.DeviceIoControl.argtypes = [ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]

hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetProductString.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]
hid.HidD_GetManufacturerString.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]
hid.HidD_GetSerialNumberString.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_GetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.ULONG]


def open_hid(path):
    h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                             OPEN_EXISTING, 0, None)
    return None if (not h or h == INVALID_HANDLE_VALUE) else h


def wide_string(getter, handle):
    buf = ctypes.create_unicode_buffer(256)
    if getter(handle, buf, ctypes.sizeof(buf)):
        return buf.value
    return ""


def read_report_descriptor(handle):
    buf = ctypes.create_string_buffer(4096)
    returned = wintypes.DWORD(0)
    ok = kernel32.DeviceIoControl(handle, IOCTL_HID_GET_REPORT_DESCRIPTOR, None, 0,
                                  buf, ctypes.sizeof(buf), ctypes.byref(returned), None)
    if not ok or returned.value == 0:
        return b""
    return buf.raw[:returned.value]


def main():
    print("=" * 78)
    print(" HID game controller probe")
    print("=" * 78)

    guid = GUID.from_buffer_copy(GUID_DEVINTERFACE_HID)
    flags = DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None, flags)
    if hdev == INVALID_HANDLE_VALUE or hdev == -1:
        print("SetupDiGetClassDevs failed:", ctypes.get_last_error())
        return 1

    found = 0
    idx = 0
    try:
        while True:
            iface = SP_DEVICE_INTERFACE_DATA()
            iface.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
            if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid), idx, ctypes.byref(iface)):
                break
            idx += 1

            detail = SP_DEVICE_INTERFACE_DETAIL_DATA_W()
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
                attrs = HIDD_ATTRIBUTES()
                attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
                if not hid.HidD_GetAttributes(h, ctypes.byref(attrs)):
                    continue
                caps = HIDP_CAPS()
                pp = ctypes.c_void_p()
                has_caps = bool(hid.HidD_GetPreparsedData(h, ctypes.byref(pp)))
                if has_caps:
                    has_caps = bool(hid.HidP_GetCaps(pp, ctypes.byref(caps)))
                    hid.HidD_FreePreparsedData(pp)

                product = wide_string(hid.HidD_GetProductString, h)
                vendor = wide_string(hid.HidD_GetManufacturerString, h)
                serial = wide_string(hid.HidD_GetSerialNumberString, h)

                is_gamepad = has_caps and caps.UsagePage == 0x01 and caps.Usage in (0x04, 0x05)
                looks_like_pad = any(k in (product + vendor).lower()
                                     for k in ("gamesir", "gamepad", "controller", "xbox", "joystick"))
                if not (is_gamepad or looks_like_pad):
                    continue

                found += 1
                print("\n--- %s ---" % path)
                print("  VID:PID      %04X:%04X   ver %04X" % (attrs.VendorID, attrs.ProductID, attrs.VersionNumber))
                print("  Manufacturer %s" % (vendor or "<none>"))
                print("  Product      %s" % (product or "<none>"))
                print("  Serial       %s" % (serial or "<none>"))
                if has_caps:
                    print("  UsagePage   0x%02X  Usage 0x%02X" % (caps.UsagePage, caps.Usage))
                    print("  Reports     input=%d output=%d feature=%d" %
                          (caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))

                desc = read_report_descriptor(h)
                if desc:
                    print("  Report descriptor (%d bytes): %s" % (len(desc), desc.hex(" ")))
                    has_battery_usage = b"\x06\x06\x00" in desc and b"\x09\x20" in desc
                    print("  Standard Battery Strength usage declared: %s" %
                          ("YES -> try Windows.Devices.Power / HidD_GetFeature" if has_battery_usage
                           else "NO  -> battery must come from a vendor-specific report"))

                if has_caps and caps.FeatureReportByteLength > 0:
                    length = max(caps.FeatureReportByteLength, 64)
                    print("  Feature report probe (id -> bytes):")
                    for rid in range(0, 16):
                        buf = ctypes.create_string_buffer(length)
                        buf[0] = bytes([rid])
                        if hid.HidD_GetFeature(h, buf, length):
                            data = buf.raw[:min(length, 16)]
                            if any(data[1:]):
                                print("    id 0x%02X: %s" % (rid, data.hex(" ")))
            finally:
                kernel32.CloseHandle(h)
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hdev)

    if found == 0:
        print("\nNo HID game controller interface found.")
        print("Check: receiver plugged in, controller powered on, and that it shows up in")
        print("       Device Manager > Human Interface Devices.")

    print("\n" + "=" * 78)
    print(" XInput")
    print("=" * 78)
    try:
        xinput = ctypes.WinDLL("xinput1_4")
    except OSError:
        xinput = ctypes.WinDLL("xinput1_3")

    class XINPUT_STATE(ctypes.Structure):
        _fields_ = [("dwPacketNumber", wintypes.DWORD), ("Gamepad", ctypes.c_byte * 12)]

    class XINPUT_BATTERY_INFORMATION(ctypes.Structure):
        _fields_ = [("BatteryType", ctypes.c_ubyte), ("BatteryLevel", ctypes.c_ubyte)]

    battery_types = {0: "DISCONNECTED", 1: "WIRED", 2: "ALKALINE", 3: "NIMH", 4: "UNKNOWN"}
    battery_levels = {0: "EMPTY", 1: "LOW", 2: "MEDIUM", 3: "FULL"}

    any_connected = False
    for slot in range(4):
        state = XINPUT_STATE()
        if xinput.XInputGetState(slot, ctypes.byref(state)) != 0:
            continue
        any_connected = True
        info = XINPUT_BATTERY_INFORMATION()
        rc = xinput.XInputGetBatteryInformation(slot, 0, ctypes.byref(info))
        if rc == 0:
            print("  slot %d connected | battery: type=%s level=%s" % (
                slot, battery_types.get(info.BatteryType, info.BatteryType),
                battery_levels.get(info.BatteryLevel, info.BatteryLevel)))
        else:
            print("  slot %d connected | XInputGetBatteryInformation failed (%d)" % (slot, rc))

    if not any_connected:
        print("  no XInput controller connected")

    print("\nDone.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
