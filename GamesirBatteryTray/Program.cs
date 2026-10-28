using System.Threading;

namespace GamesirBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase)))
        {
            var (devices, error) = GameSirDevice.ReadAll();
            if (devices.Count == 0)
            {
                Console.WriteLine($"no device connected ({error ?? "unknown"})");
                return 1;
            }

            foreach (var device in devices)
                Console.WriteLine($"mac={device.Mac} percent={device.Percent}");

            return 0;
        }

        using var mutex = new Mutex(true, @"Local\GameSirBatteryTray", out var isFirstInstance);
        if (!isFirstInstance) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        return 0;
    }
}
