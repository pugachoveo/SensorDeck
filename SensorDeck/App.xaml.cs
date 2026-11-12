using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SensorDeck.Services;
using SensorDeck.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SensorDeck;

public partial class App : Application
{
    private const string MutexName = "SensorDeck.SingleInstance";

    private static Mutex? _instanceMutex;
    private static bool _ownsMutex;
    private HardwareService? _hardware;
    private MainViewModel? _vm;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private HotkeyService? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private Drawing.Icon? _icon;

    public static bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "SensorDeck error");
            ex.Handled = true;
        };

        var args = e.Args;
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--dump") is { } dumpPath)
        {
            Dump(dumpPath);
            Shutdown(0);
            return;
        }

        var snapDir = Arg("--snap");

        _instanceMutex = new Mutex(true, MutexName, out var isFirst);
        _ownsMutex = isFirst;
        if (!isFirst && snapDir is null)
        {
            MessageBox.Show("SensorDeck is already running. Look for its icon in the system tray.", "SensorDeck");
            Shutdown(0);
            return;
        }

        var settingsPath = Arg("--settings") ?? (snapDir is null ? null : Path.Combine(snapDir, "settings.json"));
        _hardware = new HardwareService();
        _vm = new MainViewModel(_hardware, new SettingsService(settingsPath));

        _icon = CreateAppIcon();
        var iconSource = Imaging.CreateBitmapSourceFromHIcon(_icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        _main = new MainWindow(_vm) { Icon = iconSource };
        _overlay = new OverlayWindow(_vm);
        _overlay.OpenMainRequested += ShowMain;

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += () => _vm.IsOverlayVisible = !_vm.IsOverlayVisible;
        _vm.TryRegisterHotkey = _hotkeys.Register;
        // In --snap test mode a normal copy may already own the hotkey; do not report that as an error.
        if (!_hotkeys.Register(_vm.Settings.HotkeyModifiers, _vm.Settings.HotkeyKey) && snapDir is null)
            _vm.HotkeyError = $"{_vm.HotkeyText} is used by another app. Pick a different shortcut";

        CreateTray();

        _main.Show();
        _overlay.SyncVisibility();

        try
        {
            await _vm.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not initialize sensors:\n\n" + ex.Message, "SensorDeck");
        }

        if (snapDir is not null) await SnapAndExit(snapDir);
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open SensorDeck", null, (_, _) => ShowMain());
        var toggle = menu.Items.Add("Show / hide overlay", null, (_, _) => _vm!.IsOverlayVisible = !_vm.IsOverlayVisible);
        var lockItem = new Forms.ToolStripMenuItem("Lock overlay (click-through)") { CheckOnClick = true };
        lockItem.Click += (_, _) => _vm!.ClickThrough = lockItem.Checked;
        menu.Items.Add(lockItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            lockItem.Checked = _vm!.ClickThrough;
            toggle.Text = (_vm.IsOverlayVisible ? "Hide overlay" : "Show overlay") + $"   ({_vm.HotkeyText})";
        };

        _tray = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "SensorDeck",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowMain(); };
    }

    public void ExitApp()
    {
        IsExiting = true;
        _vm?.Dispose();
        _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _overlay?.Close();
        _main?.Close();
        _hardware?.Dispose();
        ReleaseInstanceLock();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!IsExiting)
        {
            _vm?.Dispose();
            _hardware?.Dispose();
            _tray?.Dispose();
        }
        base.OnExit(e);
    }

    private static void ReleaseInstanceLock()
    {
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _ownsMutex = false;
        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }

    public static void RestartElevated()
    {
        try
        {
            // Free the single-instance lock so the elevated copy can start.
            ReleaseInstanceLock();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            ((App)Current).ExitApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not restart: " + ex.Message, "SensorDeck");
        }
    }

    /// <summary>Small meter icon drawn at runtime: three bars, green / amber / red.</summary>
    private static Drawing.Icon CreateAppIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0x1D, 0x23, 0x30));
            using var path = new Drawing.Drawing2D.GraphicsPath();
            const int r = 8;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(31 - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(31 - r * 2, 31 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, 31 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(bg, path);
            using var b1 = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x3D, 0xD6, 0x8C));
            using var b2 = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xFF, 0xB5, 0x47));
            using var b3 = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xFF, 0x5C, 0x5C));
            g.FillRectangle(b1, 7, 17, 5, 9);
            g.FillRectangle(b2, 14, 11, 5, 15);
            g.FillRectangle(b3, 21, 6, 5, 20);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- diagnostics ----------

    /// <summary>--dump file: write every sensor and value to a text file, no UI.</summary>
    private static void Dump(string path)
    {
        using var hw = new HardwareService();
        hw.Open();
        hw.Update();
        Thread.Sleep(1000);
        hw.Update();

        var sb = new StringBuilder();
        sb.AppendLine($"Admin: {new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}");
        sb.AppendLine($"PawnIO: {PawnIoInstaller.InstalledVersion()?.ToString() ?? "not installed"} ({PawnIoInstaller.GetState()}), bundled {PawnIoInstaller.BundledVersion}");
        using (var res = typeof(PawnIoInstaller).Assembly.GetManifestResourceStream("SensorDeck.PawnIO_setup.exe"))
            sb.AppendLine($"Embedded installer: {res?.Length ?? 0} bytes");
        foreach (var (h, name) in hw.GetAllHardware())
        {
            sb.AppendLine($"[{h.HardwareType}] {name}");
            foreach (var s in h.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
                sb.AppendLine($"    {s.SensorType,-12} {s.Name,-40} {SensorFormat.Format(s.SensorType, s.Value),12}   {s.Identifier}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>--snap dir: add demo sensors, render both windows to PNG and exit (used for testing).</summary>
    private async Task SnapAndExit(string dir)
    {
        Directory.CreateDirectory(dir);
        _vm!.AddDemoSensors();
        _vm.IsOverlayVisible = true;
        await Task.Delay(2500);
        Render(_main!, Path.Combine(dir, "main.png"));
        Render(_overlay!, Path.Combine(dir, "overlay.png"));
        ExitApp();
    }

    private static void Render(Window w, string file)
    {
        if (w.Content is not FrameworkElement root || root.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(root);
        var rtb = new RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (w.Background is SolidColorBrush { Color.A: > 0 } bg)
                dc.DrawRectangle(bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            else
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x80)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
