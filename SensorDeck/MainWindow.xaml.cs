using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SensorDeck.Services;
using SensorDeck.ViewModels;

namespace SensorDeck;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Closing the window only hides it to the tray; the app keeps running.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private static PanelItem ItemOf(object sender) => (PanelItem)((FrameworkElement)sender).DataContext;

    private void Remove_Click(object sender, RoutedEventArgs e) => _vm.RemoveFromPanel(ItemOf(sender));
    private void MoveUp_Click(object sender, RoutedEventArgs e) => _vm.MovePanelItem(ItemOf(sender), -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _vm.MovePanelItem(ItemOf(sender), +1);
    private void ToggleOverlay_Click(object sender, RoutedEventArgs e) => _vm.IsOverlayVisible = !_vm.IsOverlayVisible;

    // ---------- hotkey capture ----------

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = "Press a combination…";
        HotkeyHint.Text = "Esc to cancel";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = _vm.HotkeyText;
        HotkeyHint.Text = "Click the field and press a combination, e.g. Ctrl + Alt + H";
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            SensorTree.Focus();
            return;
        }

        // Wait until a non-modifier key is pressed.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return;

        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;

        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (mods == 0 && !isFunctionKey)
        {
            _vm.HotkeyError = "Use a modifier (Ctrl, Alt, Shift or Win) or one of F1–F24";
            return;
        }

        _vm.SetHotkey(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        SensorTree.Focus();
    }

    // ---------- banners ----------

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e) => App.RestartElevated();

    private async void InstallPawnIo_Click(object sender, RoutedEventArgs e) => await _vm.InstallPawnIoAsync();
}
