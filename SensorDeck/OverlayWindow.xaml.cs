using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SensorDeck.Services;
using SensorDeck.ViewModels;

namespace SensorDeck;

/// <summary>Borderless, always-on-top, non-activating panel with the chosen sensors.</summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;

    public event Action? OpenMainRequested;

    public OverlayWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Left = vm.Settings.OverlayLeft;
        Top = vm.Settings.OverlayTop;

        vm.PropertyChanged += OnVmChanged;

        // Other "topmost" windows (task manager, some launchers) can end up above us; re-assert periodically.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => BringToTop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Tool window: not in Alt+Tab. No-activate: clicking it never steals focus from a game/app.
        var ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        ApplyClickThrough();
        KeepOnScreen();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            _vm.IsOverlayVisible = false;
        }
        base.OnClosing(e);
    }

    public void SyncVisibility()
    {
        if (_vm.IsOverlayVisible)
        {
            Show();
            BringToTop();
            _topmostTimer.Start();
        }
        else
        {
            _topmostTimer.Stop();
            Hide();
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ClickThrough): ApplyClickThrough(); break;
            case nameof(MainViewModel.IsOverlayVisible): SyncVisibility(); break;
        }
    }

    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        ex = _vm.ClickThrough
            ? ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED
            : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
    }

    private void BringToTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>If the saved position is off-screen (monitor unplugged), move the panel back.</summary>
    private void KeepOnScreen()
    {
        var l = SystemParameters.VirtualScreenLeft;
        var t = SystemParameters.VirtualScreenTop;
        var r = l + SystemParameters.VirtualScreenWidth;
        var b = t + SystemParameters.VirtualScreenHeight;
        if (Left < l || Left > r - 60 || Top < t || Top > b - 30)
        {
            Left = 40;
            Top = 40;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm.ClickThrough) return;
        DragMove();
        _vm.SetOverlayPosition(Left, Top);
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => _vm.ClickThrough = true;
    private void Hide_Click(object sender, RoutedEventArgs e) => _vm.IsOverlayVisible = false;
    private void Open_Click(object sender, RoutedEventArgs e) => OpenMainRequested?.Invoke();
}
