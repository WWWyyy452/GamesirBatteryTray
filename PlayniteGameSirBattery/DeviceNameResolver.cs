using System;
using System.Collections.Generic;
using System.IO;
using Playnite.SDK.Data;

namespace GameSirBattery
{
    /// <summary>
    /// Reuses the names configured by the tray app (%AppData%\GameSirBatteryTray\devices.json)
    /// so both tools show the same label for a controller.
    /// </summary>
    internal static class DeviceNameResolver
    {
        private const string ConfigRelativePath = @"GameSirBatteryTray\devices.json";
        private static readonly object Gate = new object();
        private static Dictionary<string, string> names;

        public static string Resolve(string mac)
        {
            lock (Gate)
            {
                if (names == null)
                {
                    names = Load();
                }

                string name;
                if (names.TryGetValue(mac, out name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                var compact = mac.Replace(":", string.Empty);
                var suffix = compact.Length > 4 ? compact.Substring(compact.Length - 4) : compact;
                return "手柄 " + suffix;
            }
        }

        private static Dictionary<string, string> Load()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ConfigRelativePath);

                if (!File.Exists(path))
                {
                    return result;
                }

                var entries = Serialization.FromJson<List<DeviceNameEntry>>(File.ReadAllText(path));
                if (entries == null)
                {
                    return result;
                }

                foreach (var entry in entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Mac))
                    {
                        result[entry.Mac] = entry.Name;
                    }
                }
            }
            catch
            {
                // A missing or malformed config only means we fall back to MAC suffixes.
            }

            return result;
        }

        public class DeviceNameEntry
        {
            public string Mac { get; set; }
            public string Name { get; set; }
        }
    }
}
