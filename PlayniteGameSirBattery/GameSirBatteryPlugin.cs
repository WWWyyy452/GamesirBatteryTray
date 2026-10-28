using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace GameSirBattery
{
    /// <summary>
    /// Shows GameSir controller battery level inside Playnite as a small always-on-top overlay,
    /// plus a sidebar entry that opens the same information.
    /// </summary>
    public class GameSirBatteryPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly string DebugLogPath =
            Path.Combine(Path.GetTempPath(), "gamesir_playnite.log");

        private const int RefreshSeconds = 30;
        private const int FirstRefreshDelaySeconds = 5;

        private readonly SidebarItem sidebarItem;
        private readonly Timer refreshTimer;
        private volatile ScanResult lastScan = new ScanResult();
        private Window overlay;
        private TextBlock overlayText;
        private string lastText;

        public override Guid Id { get; } = Guid.Parse("6f0d2f8e-3b6a-4a1e-9c2d-71b0a4c5e6f7");

        public GameSirBatteryPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };

            sidebarItem = new SidebarItem
            {
                Title = "手柄电量",
                Icon = "SidebarLibraryIcon",
                Type = SiderbarItemType.Button,
                Activated = ShowBatteryDialog,
                Visible = true
            };

            CreateOverlay();
            Log("plugin constructed");

            refreshTimer = new Timer(OnTimerElapsed, null,
                TimeSpan.FromSeconds(FirstRefreshDelaySeconds), TimeSpan.FromSeconds(RefreshSeconds));
        }

        private void CreateOverlay()
        {
            try
            {
                overlayText = new TextBlock
                {
                    Text = "手柄电量  --",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x14, 0x28)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x6C, 0x7A, 0x99)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 8, 14, 8),
                    Child = overlayText
                };

                overlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = border,
                    ToolTip = "拖到任意位置，右键关闭"
                };

                var area = SystemParameters.WorkArea;
                overlay.Left = area.Right - 260;
                overlay.Top = area.Bottom - 90;

                overlay.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ButtonState == MouseButtonState.Pressed)
                    {
                        overlay.DragMove();
                    }
                };

                var menu = new ContextMenu();
                var close = new MenuItem { Header = "关闭浮窗" };
                close.Click += (_, __) => overlay.Hide();
                var show = new MenuItem { Header = "显示浮窗" };
                show.Click += (_, __) => overlay.Show();
                menu.Items.Add(show);
                menu.Items.Add(close);
                overlay.ContextMenu = menu;

                overlay.Show();
                Log("overlay shown at " + overlay.Left + "," + overlay.Top);
            }
            catch (Exception ex)
            {
                Log("overlay failed: " + ex);
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            Log("GetSidebarItems requested");
            yield return sidebarItem;
        }

        private void OnTimerElapsed(object state)
        {
            try
            {
                var scan = GameSirDevice.ReadAll();
                lastScan = scan;

                var text = BuildText(scan);
                Log(string.Format("scan: devices={0} error={1} text={2}",
                    scan.Devices.Count, scan.Error ?? "-", text));

                var dispatcher = PlayniteApi.MainView.UIDispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (overlayText != null)
                        {
                            overlayText.Text = text;
                            overlayText.Foreground = new SolidColorBrush(OverallColor(scan));
                        }

                        sidebarItem.Title = text;
                    }
                    catch (Exception ex)
                    {
                        Log("ui update failed: " + ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                Log("scan failed: " + ex);
            }
        }

        private static string BuildText(ScanResult scan)
        {
            if (scan == null || scan.Devices.Count == 0)
            {
                return "手柄  --";
            }

            return "手柄  " + string.Join("   ", scan.Devices.Select(d =>
                DeviceNameResolver.Resolve(d.Mac) + " " + d.Percent + "%"));
        }

        private static Color OverallColor(ScanResult scan)
        {
            if (scan == null || scan.Devices.Count == 0)
            {
                return Color.FromRgb(0x8A, 0x90, 0x9A);
            }

            var lowest = scan.Devices.Min(d => d.Percent);
            if (lowest >= 50) return Color.FromRgb(0x3D, 0xD6, 0x8C);
            if (lowest >= 20) return Color.FromRgb(0xFF, 0xB0, 0x20);
            return Color.FromRgb(0xFF, 0x5A, 0x5A);
        }

        private void ShowBatteryDialog()
        {
            var scan = lastScan;
            string message;

            if (scan == null || scan.Devices.Count == 0)
            {
                message = "未检测到 GameSir 手柄。\n请确认 2.4G 接收器已插入、手柄已开机。";
            }
            else
            {
                message = string.Join("\n", scan.Devices.Select(d =>
                    DeviceNameResolver.Resolve(d.Mac) + "：" + d.Percent + "%"));
            }

            PlayniteApi.Dialogs.ShowMessage(message, "GameSir 手柄电量");
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(DebugLogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never break the plugin.
            }

            try
            {
                Logger.Info(message);
            }
            catch
            {
            }
        }

        public override void Dispose()
        {
            if (refreshTimer != null)
            {
                refreshTimer.Dispose();
            }

            try
            {
                if (overlay != null)
                {
                    overlay.Close();
                    overlay = null;
                }
            }
            catch
            {
            }

            base.Dispose();
        }
    }
}
