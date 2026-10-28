using System;
using System.Text;

namespace GameSirBattery
{
    /// <summary>
    /// Out-of-process battery reader. Playnite's own controller handling consumes HID input
    /// reports, so the plugin shells out to this exe instead of reading the device itself.
    /// Output: one "mac|percent" line per controller.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                var scan = GameSirDevice.ReadAll();
                foreach (var device in scan.Devices)
                {
                    Console.WriteLine(device.Mac + "|" + device.Percent);
                }

                return scan.Devices.Count > 0 ? 0 : 1;
            }
            catch
            {
                return 2;
            }
        }
    }
}
