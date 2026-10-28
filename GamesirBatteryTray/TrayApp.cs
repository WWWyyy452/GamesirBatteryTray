using Microsoft.Win32;

namespace GamesirBatteryTray;

/// <summary>
/// One tray icon per controller. Each receiver is identified by the MAC it reports,
/// so names and colours stay stable across reconnects.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "GameSirBatteryTray";
    private const string PlaceholderMac = "";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 168, 174, 182), // 灰
        Color.FromArgb(255, 255, 111, 165), // 粉
        Color.FromArgb(255, 96, 165, 250),  // 蓝
        Color.FromArgb(255, 251, 191, 36)   // 黄
    };

    private readonly DeviceRegistry _registry;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _timer;
    private bool _refreshing;

    public TrayApp()
    {
        _registry = DeviceRegistry.Load();
        foreach (var entry in _registry.Entries)
            EnsureSlot(entry.Mac, entry.Name);

        _timer = new System.Windows.Forms.Timer { Interval = (int)RefreshInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private sealed class Slot
    {
        public required string Mac { get; init; }
        public required int Order { get; init; }
        public required NotifyIcon TrayIcon { get; init; }
        public required ToolStripMenuItem StatusItem { get; init; }
        public string Name { get; set; } = string.Empty;
    }

    private Slot EnsureSlot(string mac, string name)
    {
        if (_slots.TryGetValue(mac, out var existing))
        {
            existing.Name = name;
            return existing;
        }

        var order = _slots.Count;
        var accent = Palette[order % Palette.Length];

        var statusItem = new ToolStripMenuItem("正在检测…") { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        var deviceItem = new ToolStripMenuItem(string.IsNullOrEmpty(mac) ? "未检测到手柄" : $"设备 {mac}") { Enabled = false };
        var refreshItem = new ToolStripMenuItem("立即刷新", null, (_, _) => _ = RefreshAsync()) { ShortcutKeyDisplayString = "双击图标" };
        var renameItem = new ToolStripMenuItem("重命名…", null, (_, _) => Rename(mac))
        {
            Enabled = !string.IsNullOrEmpty(mac)
        };
        var startupItem = new ToolStripMenuItem("开机自动启动", null, (_, _) => ToggleStartup());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            statusItem,
            deviceItem,
            new ToolStripSeparator(),
            refreshItem,
            renameItem,
            startupItem,
            new ToolStripSeparator(),
            exitItem
        });
        menu.Opening += (_, _) => startupItem.Checked = IsStartupEnabled();

        var notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = TrayIconFactory.Create(null, false, accent),
            Text = name,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => _ = RefreshAsync();

        var slot = new Slot
        {
            Mac = mac,
            Order = order,
            TrayIcon = notifyIcon,
            StatusItem = statusItem,
            Name = name
        };

        notifyIcon.Text = name;
        _slots[mac] = slot;
        return slot;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var (devices, error) = await Task.Run(GameSirDevice.ReadAll);
            var online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices)
            {
                var mac = device.Mac!;
                var name = _registry.ResolveName(mac);
                var slot = EnsureSlot(mac, name);
                online.Add(mac);
                UpdateSlot(slot, device.Percent, true);
            }

            foreach (var slot in _slots.Values.Where(s => !online.Contains(s.Mac)))
                UpdateSlot(slot, null, false);

            if (_slots.ContainsKey(PlaceholderMac) && _slots.Count > 1)
                RemoveSlot(PlaceholderMac);

            if (_slots.Count == 0)
                UpdateSlot(EnsureSlot(PlaceholderMac, "手柄"), null, false);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateSlot(Slot slot, int? percent, bool connected)
    {
        var accent = Palette[slot.Order % Palette.Length];
        var icon = TrayIconFactory.Create(percent, connected, accent);
        var previous = slot.TrayIcon.Icon;
        slot.TrayIcon.Icon = icon;
        previous?.Dispose();

        if (connected && percent is int value)
        {
            slot.TrayIcon.Text = $"{slot.Name} · {value}%";
            slot.StatusItem.Text = $"{slot.Name} · {value}%";
        }
        else
        {
            slot.TrayIcon.Text = $"{slot.Name} 未连接";
            slot.StatusItem.Text = $"{slot.Name} 未连接";
        }
    }

    private void RemoveSlot(string mac)
    {
        if (!_slots.Remove(mac, out var slot)) return;
        slot.TrayIcon.Visible = false;
        slot.TrayIcon.Icon?.Dispose();
        slot.TrayIcon.Dispose();
    }

    private void Rename(string mac)
    {
        if (string.IsNullOrEmpty(mac) || !_slots.TryGetValue(mac, out var slot)) return;

        using var dialog = new RenameDialog(slot.Name, mac);
        if (dialog.ShowDialog() != DialogResult.OK || dialog.Value.Length == 0) return;

        slot.Name = dialog.Value;
        _registry.Rename(mac, dialog.Value);
        _ = RefreshAsync();
    }

    private void ToggleStartup()
    {
        try
        {
            SetStartup(!IsStartupEnabled());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法修改开机启动设置：{ex.Message}", "启明星2 电量",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string value && value.Length > 0;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("无法确定程序路径");
            key.SetValue(RunValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();

            foreach (var slot in _slots.Values)
            {
                slot.TrayIcon.Visible = false;
                slot.TrayIcon.Icon?.Dispose();
                slot.TrayIcon.Dispose();
            }

            _slots.Clear();
        }

        base.Dispose(disposing);
    }
}
