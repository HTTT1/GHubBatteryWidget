using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool created;
        using (Mutex singleInstance = new Mutex(true, @"Local\GHubBatteryWidget-7A78D62E", out created))
        {
            if (!created)
            {
                Forms.MessageBox.Show("G HUB 電量小工具已經在執行。", "G HUB 電量小工具",
                    Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
                return;
            }
            Application app = new Application();
            app.Run(new BatteryWidget());
        }
    }
}

internal sealed class BatteryWidget : Window
{
    private const int MaxMessageBytes = 1024 * 1024;
    private const int WsExTransparent = 0x20;
    private const int GwlExStyle = -20;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "GHubBatteryWidget";

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly string configPath;
    private readonly CancellationTokenSource stopping = new CancellationTokenSource();
    private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
    private readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
    private readonly Forms.ToolStripMenuItem statusItem = new Forms.ToolStripMenuItem("正在連接 G HUB…");
    private readonly Forms.ToolStripMenuItem topMostItem = new Forms.ToolStripMenuItem("永遠置頂");
    private readonly Forms.ToolStripMenuItem clickThroughItem = new Forms.ToolStripMenuItem("滑鼠穿透");
    private readonly Forms.ToolStripMenuItem startupItem = new Forms.ToolStripMenuItem("隨 Windows 啟動");
    private readonly Forms.ToolStripMenuItem showItem = new Forms.ToolStripMenuItem("顯示 / 隱藏");
    private readonly Forms.ToolStripMenuItem refreshItem = new Forms.ToolStripMenuItem("立即更新");
    private readonly Forms.ToolStripMenuItem backgroundMenu = new Forms.ToolStripMenuItem("背景透明度");

    private readonly Border card = new Border();
    private readonly TextBlock titleText = new TextBlock();
    private readonly TextBlock connectionIcon = new TextBlock();
    private readonly TextBlock connectionText = new TextBlock();
    private readonly TextBlock batteryIcon = new TextBlock();
    private readonly TextBlock chargingIcon = new TextBlock();
    private readonly TextBlock batteryText = new TextBlock();
    private readonly TextBlock detailText = new TextBlock();

    private bool clickThrough;
    private bool changingStartup;
    private bool forceRefresh;
    private bool hasSavedPosition;
    private int savedX;
    private int savedY;
    private int backgroundOpacity = 85;
    private string deviceName = "PRO X SUPERLIGHT";
    private string connection = "LIGHTSPEED";
    private int battery = -1;
    private bool charging;
    private double hoursLeft = -1;

    public BatteryWidget()
    {
        configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GHubBatteryWidget.ini");
        Title = "G HUB 電量小工具";
        Width = 360;
        Height = 88;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");

        BuildVisualTree();
        LoadConfig();
        ApplyBackground();
        BuildMenu();

        tray.Text = "G HUB 電量小工具";
        tray.Icon = CreateTrayIcon(-1, false);
        tray.Visible = true;
        tray.ContextMenuStrip = menu;
        tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke((Action)ToggleVisible);
        };

        MouseLeftButtonDown += OnDrag;
        MouseRightButtonUp += delegate { menu.Show(Forms.Cursor.Position); };
        SourceInitialized += delegate
        {
            RestorePosition();
            ApplyClickThrough();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        };
        Loaded += delegate
        {
            Task.Run((Func<Task>)PollLoop);
        };
        Closing += OnClosing;
    }

    private void BuildVisualTree()
    {
        card.CornerRadius = new CornerRadius(28);
        card.BorderThickness = new Thickness(1.15);
        card.Padding = new Thickness(22, 14, 22, 13);
        Content = card;

        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.Child = root;

        titleText.Text = deviceName;
        titleText.FontFamily = new FontFamily("Segoe UI Semibold");
        titleText.FontSize = 14.5;
        titleText.Foreground = NewBrush(245, 248, 247);
        titleText.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(titleText);

        Grid status = new Grid { Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(status, 1);
        root.Children.Add(status);

        ConfigureStatusText(connectionIcon, 13, NewBrush(225, 232, 230));
        ConfigureStatusText(connectionText, 11.5, NewBrush(225, 232, 230));
        ConfigureStatusText(batteryIcon, 14, NewBrush(232, 239, 237));
        ConfigureStatusText(chargingIcon, 12, NewBrush(95, 193, 255));
        ConfigureStatusText(batteryText, 11.5, NewBrush(232, 239, 237));
        ConfigureStatusText(detailText, 11.5, NewBrush(166, 179, 176));

        connectionIcon.Text = "⌁";
        connectionIcon.Margin = new Thickness(0, 0, 6, 0);
        connectionText.Text = connection;
        batteryIcon.Text = "▣";
        batteryIcon.Margin = new Thickness(0, 0, 6, 0);
        chargingIcon.Text = "";
        chargingIcon.Margin = new Thickness(0, 0, 3, 0);
        batteryText.Text = "--%";
        detailText.Text = "正在連接 G HUB…";

        AddAt(status, connectionIcon, 0);
        AddAt(status, connectionText, 1);
        AddAt(status, batteryIcon, 3);
        AddAt(status, chargingIcon, 4);
        AddAt(status, batteryText, 5);
        AddAt(status, detailText, 7);
    }

    private static void ConfigureStatusText(TextBlock text, double size, Brush color)
    {
        text.FontSize = size;
        text.Foreground = color;
        text.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void AddAt(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static SolidColorBrush NewBrush(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void BuildMenu()
    {
        statusItem.Enabled = false;
        statusItem.Font = new Drawing.Font(statusItem.Font, Drawing.FontStyle.Bold);
        showItem.Click += delegate { Dispatcher.BeginInvoke((Action)ToggleVisible); };
        refreshItem.Click += delegate { forceRefresh = true; };

        topMostItem.Checked = Topmost;
        topMostItem.CheckOnClick = true;
        topMostItem.CheckedChanged += delegate
        {
            Topmost = topMostItem.Checked;
            SaveConfig();
        };

        clickThroughItem.Checked = clickThrough;
        clickThroughItem.CheckOnClick = true;
        clickThroughItem.CheckedChanged += delegate
        {
            clickThrough = clickThroughItem.Checked;
            ApplyClickThrough();
            SaveConfig();
            if (clickThrough)
                tray.ShowBalloonTip(2500, "滑鼠穿透已開啟", "請用系統匣圖示叫出選單或關閉穿透。", Forms.ToolTipIcon.Info);
        };

        AddBackgroundItem("實色背景", 100);
        AddBackgroundItem("輕微透明 85%", 85);
        AddBackgroundItem("半透明 70%", 70);
        AddBackgroundItem("高度透明 55%", 55);
        backgroundMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        AddBackgroundItem("背景完全透明（只留資訊）", 0);

        startupItem.Checked = IsStartupEnabled();
        startupItem.CheckOnClick = true;
        startupItem.CheckedChanged += delegate
        {
            if (!changingStartup) SetStartup(startupItem.Checked);
        };

        Forms.ToolStripMenuItem exitItem = new Forms.ToolStripMenuItem("結束");
        exitItem.Click += delegate { Dispatcher.BeginInvoke((Action)Close); };
        Forms.ToolStripMenuItem moveHereItem = new Forms.ToolStripMenuItem("移到目前螢幕右下角");
        moveHereItem.Click += delegate
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                MoveToCursorScreen();
                SaveConfig();
            });
        };

        menu.Items.Add(statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(backgroundMenu);
        menu.Items.Add(topMostItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(moveHereItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
    }

    private void AddBackgroundItem(string label, int percent)
    {
        Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(label);
        item.Tag = percent;
        item.Checked = backgroundOpacity == percent;
        item.Click += delegate
        {
            backgroundOpacity = (int)item.Tag;
            foreach (Forms.ToolStripItem child in backgroundMenu.DropDownItems)
            {
                Forms.ToolStripMenuItem choice = child as Forms.ToolStripMenuItem;
                if (choice != null) choice.Checked = Object.ReferenceEquals(choice, item);
            }
            Dispatcher.BeginInvoke((Action)delegate
            {
                ApplyBackground();
                SaveConfig();
            });
        };
        backgroundMenu.DropDownItems.Add(item);
    }

    private void ApplyBackground()
    {
        byte alpha = (byte)Math.Round(255.0 * backgroundOpacity / 100.0);
        card.Background = new SolidColorBrush(Color.FromArgb(alpha, 34, 42, 40));
        card.BorderBrush = backgroundOpacity == 0
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb((byte)Math.Min(100, (int)alpha), 255, 255, 255));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (clickThrough || e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        SnapToScreenEdge();
        SaveConfig();
    }

    private void SnapToScreenEdge()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeRect rect;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out rect)) return;
        Drawing.Rectangle windowRect = Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        Drawing.Rectangle area = Forms.Screen.FromRectangle(windowRect).WorkingArea;
        const int snap = 16;
        int x = rect.Left;
        int y = rect.Top;
        if (Math.Abs(rect.Left - area.Left) <= snap) x = area.Left;
        else if (Math.Abs(rect.Right - area.Right) <= snap) x = area.Right - windowRect.Width;
        if (Math.Abs(rect.Top - area.Top) <= snap) y = area.Top;
        else if (Math.Abs(rect.Bottom - area.Bottom) <= snap) y = area.Bottom - windowRect.Height;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void RestorePosition()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeRect rect;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out rect)) return;
        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);

        Forms.Screen target = null;
        if (hasSavedPosition)
        {
            Drawing.Point center = new Drawing.Point(savedX + width / 2, savedY + height / 2);
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                if (screen.Bounds.Contains(center)) { target = screen; break; }
            }
        }
        if (target == null) target = Forms.Screen.FromPoint(Forms.Cursor.Position);
        Drawing.Rectangle area = target.WorkingArea;
        int x = hasSavedPosition ? savedX : area.Right - width - 20;
        int y = hasSavedPosition ? savedY : area.Bottom - height - 20;
        x = Math.Max(area.Left, Math.Min(x, area.Right - width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void MoveToCursorScreen()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeRect rect;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out rect)) return;
        Drawing.Rectangle area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        SetWindowPos(handle, IntPtr.Zero, area.Right - width - 20, area.Bottom - height - 20,
            0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeRect rect;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out rect))
            {
                savedX = rect.Left;
                savedY = rect.Top;
                hasSavedPosition = true;
            }
            RestorePosition();
            SaveConfig();
        });
    }

    private async Task PollLoop()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                BatteryReading reading = await ReadFromGHub(stopping.Token).ConfigureAwait(false);
                SafeUpdate(reading, "已連線");
            }
            catch (OperationCanceledException)
            {
                if (stopping.IsCancellationRequested) break;
                SafeStatus("G HUB 無回應");
            }
            catch
            {
                SafeStatus("等待 G HUB…");
            }

            for (int i = 0; i < 5 && !stopping.IsCancellationRequested && !forceRefresh; i++)
            {
                try { await Task.Delay(1000, stopping.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            forceRefresh = false;
        }
    }

    private void SafeStatus(string status)
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            detailText.Text = status;
            UpdateTray(status);
        });
    }

    private void SafeUpdate(BatteryReading reading, string status)
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            deviceName = String.IsNullOrWhiteSpace(reading.Name) ? "LOGITECH DEVICE" : reading.Name;
            connection = String.IsNullOrWhiteSpace(reading.Connection) ? "WIRELESS" : reading.Connection.ToUpperInvariant();
            battery = reading.Percent;
            charging = reading.Charging;
            hoursLeft = reading.Hours;

            titleText.Text = deviceName.ToUpperInvariant();
            connectionText.Text = connection;
            connectionIcon.Text = connection == "USB" ? "▣" : "⌁";
            batteryText.Text = battery + "%";
            chargingIcon.Text = charging ? "ϟ" : "";

            Brush level = battery <= 15 ? NewBrush(255, 100, 92)
                : (charging ? NewBrush(95, 193, 255) : NewBrush(232, 239, 237));
            batteryIcon.Foreground = level;
            batteryText.Foreground = level;
            chargingIcon.Foreground = level;
            detailText.Text = charging ? "充電中" : (hoursLeft >= 0 ? "約 " + Math.Round(hoursLeft).ToString("0") + " 小時" : status);
            UpdateTray(status);
        });
    }

    private void UpdateTray(string status)
    {
        tray.Text = Truncate(deviceName + "  " + (battery >= 0 ? battery + "%" : status), 63);
        statusItem.Text = Truncate(deviceName + "  ·  " + connection + "  ·  " +
            (battery >= 0 ? battery + "%" + (charging ? "  充電中" : "") : status), 58);
        Drawing.Icon old = tray.Icon;
        tray.Icon = CreateTrayIcon(battery, charging);
        if (old != null) old.Dispose();
    }

    private static async Task<BatteryReading> ReadFromGHub(CancellationToken outerToken)
    {
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(outerToken))
        using (ClientWebSocket ws = new ClientWebSocket())
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            ws.Options.AddSubProtocol("json");
            ws.Options.SetRequestHeader("Origin", "file://");
            await ws.ConnectAsync(new Uri("ws://127.0.0.1:9010"), timeout.Token).ConfigureAwait(false);

            string listId = "widget-list-" + Guid.NewGuid().ToString("N");
            await Send(ws, "{\"msgId\":\"" + listId + "\",\"verb\":\"GET\",\"path\":\"/devices/list\"}", timeout.Token).ConfigureAwait(false);
            string listJson = await ReceiveFor(ws, listId, timeout.Token).ConfigureAwait(false);

            Match idMatch = Regex.Match(listJson, "\\\"id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            Match nameMatch = Regex.Match(listJson, "\\\"displayName\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            Match connectionMatch = Regex.Match(listJson, "\\\"displayConnectionType\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            if (!idMatch.Success) throw new InvalidOperationException("No Logitech battery device found");

            string deviceId = idMatch.Groups[1].Value;
            if (!Regex.IsMatch(deviceId, "^[A-Za-z0-9_-]+$"))
                throw new InvalidDataException("Invalid device identifier returned by G HUB");

            string batteryId = "widget-battery-" + Guid.NewGuid().ToString("N");
            string request = "{\"msgId\":\"" + batteryId + "\",\"verb\":\"GET\",\"path\":\"/battery/" + deviceId + "/state\"}";
            await Send(ws, request, timeout.Token).ConfigureAwait(false);
            string batteryJson = await ReceiveFor(ws, batteryId, timeout.Token).ConfigureAwait(false);

            Match percentMatch = Regex.Match(batteryJson, "\\\"percentage\\\"\\s*:\\s*([0-9]+)");
            Match chargingMatch = Regex.Match(batteryJson, "\\\"charging\\\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            Match mileageMatch = Regex.Match(batteryJson, "\\\"mileage\\\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)");
            if (!percentMatch.Success) throw new InvalidOperationException("Battery percentage unavailable");

            double parsedHours;
            BatteryReading result = new BatteryReading();
            result.Name = nameMatch.Success ? JsonUnescape(nameMatch.Groups[1].Value) : "LOGITECH DEVICE";
            result.Connection = connectionMatch.Success ? JsonUnescape(connectionMatch.Groups[1].Value) : "WIRELESS";
            result.Percent = Math.Max(0, Math.Min(100, Int32.Parse(percentMatch.Groups[1].Value)));
            result.Charging = chargingMatch.Success && chargingMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            result.Hours = mileageMatch.Success && Double.TryParse(mileageMatch.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsedHours) ? parsedHours : -1;
            return result;
        }
    }

    private static async Task Send(ClientWebSocket ws, string value, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
    }

    private static async Task<string> ReceiveFor(ClientWebSocket ws, string messageId, CancellationToken token)
    {
        while (true)
        {
            byte[] buffer = new byte[8192];
            using (MemoryStream stream = new MemoryStream())
            {
                WebSocketReceiveResult part;
                do
                {
                    part = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                        throw new IOException("G HUB closed the connection");
                    stream.Write(buffer, 0, part.Count);
                    if (stream.Length > MaxMessageBytes)
                        throw new InvalidDataException("G HUB response exceeded the safety limit");
                } while (!part.EndOfMessage);
                string message = Encoding.UTF8.GetString(stream.ToArray());
                if (message.IndexOf(messageId, StringComparison.Ordinal) >= 0) return message;
            }
        }
    }

    private void ToggleVisible()
    {
        if (IsVisible) Hide(); else { Show(); Activate(); }
    }

    private void ApplyClickThrough()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, clickThrough ? style | WsExTransparent : style & ~WsExTransparent);
    }

    private void LoadConfig()
    {
        if (!File.Exists(configPath)) return;
        try
        {
            foreach (string raw in File.ReadAllLines(configPath))
            {
                string[] pair = raw.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;
                int number;
                double decimalValue;
                bool flag;
                if (pair[0] == "X" && Int32.TryParse(pair[1], out number)) { savedX = number; hasSavedPosition = true; }
                else if (pair[0] == "Y" && Int32.TryParse(pair[1], out number)) { savedY = number; hasSavedPosition = true; }
                else if (pair[0] == "BackgroundOpacity" && Int32.TryParse(pair[1], out number)) backgroundOpacity = Math.Max(0, Math.Min(100, number));
                else if (pair[0] == "Opacity" && Double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out decimalValue)) backgroundOpacity = Math.Max(0, Math.Min(100, (int)Math.Round(decimalValue * 100)));
                else if (pair[0] == "TransparentBackground" && Boolean.TryParse(pair[1], out flag) && flag) backgroundOpacity = 0;
                else if (pair[0] == "TopMost" && Boolean.TryParse(pair[1], out flag)) Topmost = flag;
                else if (pair[0] == "ClickThrough" && Boolean.TryParse(pair[1], out flag)) clickThrough = flag;
            }
        }
        catch { }
    }

    private void SaveConfig()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeRect rect;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out rect))
            {
                savedX = rect.Left;
                savedY = rect.Top;
                hasSavedPosition = true;
            }
            string[] lines = {
                "X=" + savedX,
                "Y=" + savedY,
                "BackgroundOpacity=" + backgroundOpacity,
                "TopMost=" + Topmost,
                "ClickThrough=" + clickThrough
            };
            File.WriteAllLines(configPath, lines, Encoding.UTF8);
        }
        catch { }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                return key != null && key.GetValue(RunValueName) != null;
        }
        catch { return false; }
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled) key.SetValue(RunValueName, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else key.DeleteValue(RunValueName, false);
            }
        }
        catch
        {
            changingStartup = true;
            startupItem.Checked = !enabled;
            changingStartup = false;
            Forms.MessageBox.Show("無法變更開機啟動設定。", Title, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
        }
    }

    private static Drawing.Icon CreateTrayIcon(int percent, bool isCharging)
    {
        Drawing.Bitmap bmp = new Drawing.Bitmap(32, 32);
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);
            Drawing.Color color = percent >= 0 && percent <= 15 ? Drawing.Color.FromArgb(239, 73, 66)
                : (isCharging ? Drawing.Color.FromArgb(45, 157, 245) : Drawing.Color.FromArgb(38, 201, 108));
            using (Drawing2D.GraphicsPath path = RoundedPath(new Drawing.RectangleF(0.5f, 0.5f, 31f, 31f), 8f))
            using (Drawing.Brush bg = new Drawing.SolidBrush(color))
            using (Drawing.Pen outline = new Drawing.Pen(Drawing.Color.White, 1.6f))
            {
                g.FillPath(bg, path);
                g.DrawPath(outline, path);
            }
            string text = percent >= 0 ? percent.ToString() : "?";
            float size = text.Length >= 3 ? 11.5f : 17f;
            using (Drawing.Font font = new Drawing.Font("Segoe UI", size, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (Drawing.Brush fg = new Drawing.SolidBrush(Drawing.Color.White))
            {
                Drawing.SizeF measured = g.MeasureString(text, font);
                g.DrawString(text, font, fg, (32 - measured.Width) / 2f, (32 - measured.Height) / 2f - 1);
            }
        }
        IntPtr iconHandle = bmp.GetHicon();
        Drawing.Icon icon = (Drawing.Icon)Drawing.Icon.FromHandle(iconHandle).Clone();
        DestroyIcon(iconHandle);
        bmp.Dispose();
        return icon;
    }

    private static Drawing2D.GraphicsPath RoundedPath(Drawing.RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        Drawing2D.GraphicsPath path = new Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string JsonUnescape(string value)
    {
        return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value.Substring(0, max);
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveConfig();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        stopping.Cancel();
        tray.Visible = false;
        tray.Dispose();
    }

    private sealed class BatteryReading
    {
        public string Name;
        public string Connection;
        public int Percent;
        public bool Charging;
        public double Hours;
    }
}
