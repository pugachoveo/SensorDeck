using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using SensorDeck.Services;

namespace SensorDeck.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly HardwareService _hardware;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, SensorNode> _sensors = new();
    private readonly DispatcherTimer _saveDebounce;
    private readonly CancellationTokenSource _cts = new();
    private string _sensorSignature = "";
    private string _searchText = "";
    private string _status = "Loading sensors…";
    private bool _isLoading = true;
    private string _hotkeyText = "";
    private string? _hotkeyError;

    public MainViewModel(HardwareService hardware, SettingsService settingsService)
    {
        _hardware = hardware;
        _settingsService = settingsService;
        _settings = settingsService.Load();

        foreach (var entry in _settings.Panel)
            AddPanelItem(new PanelItem(entry.Id, entry.Label));

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveNow(); };

        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        _pawnIoState = PawnIoInstaller.GetState();
        _hotkeyText = HotkeyService.Describe(_settings.HotkeyModifiers, _settings.HotkeyKey);
    }

    public ObservableCollection<HardwareNode> Tree { get; } = new();
    public ObservableCollection<PanelItem> Panel { get; } = new();
    public AppSettings Settings => _settings;

    public bool IsAdmin { get; }
    public bool ShowAdminWarning => !IsAdmin;

    // ---------- PawnIO driver ----------

    private PawnIoState _pawnIoState;
    private bool _isInstallingPawnIo;
    private bool _rebootPending;
    private string? _pawnIoError;

    public bool ShowPawnIoWarning => IsAdmin && (_pawnIoState != PawnIoState.Ok || _rebootPending);
    public bool CanInstallPawnIo => !_isInstallingPawnIo && !_rebootPending;
    /// <summary>The button stays visible (disabled) while installing; it disappears once only a reboot is left.</summary>
    public bool CanInstallPawnIoOrBusy => !_rebootPending;

    public string PawnIoTitle => _rebootPending
        ? "PawnIO installed."
        : _pawnIoState == PawnIoState.Outdated
            ? $"PawnIO driver is outdated ({PawnIoInstaller.InstalledVersion()?.ToString(3)})."
            : "PawnIO driver is not installed.";

    public string PawnIoText => _rebootPending
        ? "Restart Windows to see CPU temperatures and motherboard fans."
        : _pawnIoError
          ?? "It is needed for CPU temperatures and motherboard fans; GPUs and drives work without it. " +
             $"The official signed installer (PawnIO {PawnIoInstaller.BundledVersion.ToString(3)}) is bundled with SensorDeck.";

    public string PawnIoButtonText => _isInstallingPawnIo
        ? "Installing…"
        : _pawnIoState == PawnIoState.Outdated ? "Update PawnIO" : "Install PawnIO";

    private void NotifyPawnIo()
    {
        foreach (var p in new[] { nameof(ShowPawnIoWarning), nameof(CanInstallPawnIo), nameof(CanInstallPawnIoOrBusy), nameof(PawnIoTitle), nameof(PawnIoText), nameof(PawnIoButtonText) })
            OnPropertyChanged(p);
    }

    public async Task InstallPawnIoAsync()
    {
        if (!CanInstallPawnIo) return;
        _isInstallingPawnIo = true;
        _pawnIoError = null;
        NotifyPawnIo();

        var result = await PawnIoInstaller.InstallAsync();

        _isInstallingPawnIo = false;
        _pawnIoState = PawnIoInstaller.GetState();
        if (!result.Success) _pawnIoError = result.Message;
        _rebootPending = result.RebootRequired;
        NotifyPawnIo();

        if (!result.Success || result.RebootRequired) return;

        // Driver is in place: re-detect hardware so CPU / motherboard sensors show up right away.
        Status = result.Message + " · reloading sensors…";
        await Task.Run(_hardware.Reopen);
        Rebuild();
        Status = result.Message + $" · {_sensors.Count} sensors";
    }

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsPanelEmpty => Panel.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ApplyFilter(); }
    }

    // ---------- overlay appearance ----------

    public double OverlayFontSize
    {
        get => _settings.OverlayFontSize;
        set { _settings.OverlayFontSize = Math.Round(value); OnPropertyChanged(); ScheduleSave(); }
    }

    public double BackgroundOpacity
    {
        get => _settings.BackgroundOpacity;
        set
        {
            _settings.BackgroundOpacity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayBackground));
            ScheduleSave();
        }
    }

    public Brush OverlayBackground
    {
        get
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(_settings.BackgroundOpacity * 255), 0x12, 0x15, 0x1B));
            brush.Freeze();
            return brush;
        }
    }

    public bool ClickThrough
    {
        get => _settings.ClickThrough;
        set { _settings.ClickThrough = value; OnPropertyChanged(); ScheduleSave(); }
    }

    public bool IsOverlayVisible
    {
        get => _settings.OverlayVisible;
        set { _settings.OverlayVisible = value; OnPropertyChanged(); ScheduleSave(); }
    }

    public string HotkeyText { get => _hotkeyText; private set => Set(ref _hotkeyText, value); }
    public string? HotkeyError { get => _hotkeyError; set => Set(ref _hotkeyError, value); }

    /// <summary>Raised when the user picks a new hotkey; App re-registers it and reports success.</summary>
    public Func<uint, uint, bool>? TryRegisterHotkey { get; set; }

    public void SetHotkey(uint modifiers, uint vk)
    {
        if (TryRegisterHotkey is null) return;
        if (TryRegisterHotkey(modifiers, vk))
        {
            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyKey = vk;
            HotkeyText = HotkeyService.Describe(modifiers, vk);
            HotkeyError = null;
            ScheduleSave();
        }
        else
        {
            HotkeyError = $"{HotkeyService.Describe(modifiers, vk)} is already used by another app";
            TryRegisterHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey); // restore previous
        }
    }

    public void SetOverlayPosition(double left, double top)
    {
        _settings.OverlayLeft = left;
        _settings.OverlayTop = top;
        ScheduleSave();
    }

    // ---------- sensors ----------

    public async Task StartAsync()
    {
        await Task.Run(() =>
        {
            _hardware.Open();
            _hardware.Update();
        });

        Rebuild();
        IsLoading = false;
        _ = Task.Run(() => UpdateLoopAsync(_cts.Token));
    }

    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, _settings.UpdateIntervalMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _hardware.Update();
                await Application.Current.Dispatcher.InvokeAsync(ApplyUpdate, DispatcherPriority.Background, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyUpdate()
    {
        var all = _hardware.GetAllHardware();
        if (Signature(all) != _sensorSignature)
        {
            Rebuild(all);
            return;
        }

        foreach (var s in _sensors.Values) s.Refresh();
        foreach (var p in Panel) p.Refresh();
        Status = $"{_sensors.Count} sensors · updated {DateTime.Now:HH:mm:ss}";
    }

    private static string Signature(List<(IHardware Hardware, string DisplayName)> all) =>
        string.Join("|", all.Select(h => h.Hardware.Identifier + ":" + h.Hardware.Sensors.Length));

    private void Rebuild(List<(IHardware Hardware, string DisplayName)>? all = null)
    {
        all ??= _hardware.GetAllHardware();
        _sensorSignature = Signature(all);

        // Remember what the user collapsed/expanded so a rebuild does not reset the tree.
        var expanded = Tree.SelectMany(h => h.Groups.Select(g => (Key: h.Name + "/" + g.Type, g.IsExpanded)))
                           .ToDictionary(x => x.Key, x => x.IsExpanded);

        foreach (var s in _sensors.Values) s.PanelToggled -= OnPanelToggled;
        _sensors.Clear();
        Tree.Clear();

        var panelIds = Panel.Select(p => p.Id).ToHashSet();

        foreach (var (hw, name) in all)
        {
            var node = new HardwareNode(name, hw.HardwareType);
            var groups = hw.Sensors
                .GroupBy(s => s.SensorType)
                .OrderBy(g => SensorFormat.GroupOrder(g.Key))
                .ThenBy(g => g.Key.ToString());

            foreach (var g in groups)
            {
                var group = new GroupNode(g.Key)
                {
                    IsExpanded = expanded.TryGetValue(name + "/" + g.Key, out var e)
                        ? e
                        : g.Key is SensorType.Temperature or SensorType.Fan,
                };

                foreach (var sensor in g.OrderBy(s => s.Index))
                {
                    // The library occasionally gives two sensors the same identifier (e.g. NVIDIA "GPU Bus" and
                    // "GPU Memory" load); disambiguate by name so each "+" toggles exactly one row.
                    var id = sensor.Identifier.ToString();
                    if (_sensors.ContainsKey(id)) id += "|" + sensor.Name;
                    var sn = new SensorNode(sensor, name, id);
                    sn.SetInPanelSilently(panelIds.Contains(sn.Id));
                    sn.Refresh();
                    sn.PanelToggled += OnPanelToggled;
                    group.Sensors.Add(sn);
                    _sensors[sn.Id] = sn;
                }
                node.Groups.Add(group);
            }

            if (node.Groups.Count > 0) Tree.Add(node);
        }

        foreach (var p in Panel) p.Sensor = _sensors.GetValueOrDefault(p.Id);
        ApplyFilter();
        Status = $"{_sensors.Count} sensors";
    }

    private void ApplyFilter()
    {
        var q = _searchText.Trim();
        bool Match(string text) => q.Length == 0 || text.Contains(q, StringComparison.OrdinalIgnoreCase);

        foreach (var hw in Tree)
        {
            var hwMatch = q.Length > 0 && Match(hw.Name);
            var anyGroup = false;
            foreach (var g in hw.Groups)
            {
                var groupMatch = hwMatch || (q.Length > 0 && Match(g.Title));
                var anySensor = false;
                foreach (var s in g.Sensors)
                {
                    s.IsVisible = groupMatch || Match(s.Name);
                    anySensor |= s.IsVisible;
                }
                g.IsVisible = anySensor;
                if (q.Length > 0 && anySensor) g.IsExpanded = true;
                anyGroup |= anySensor;
            }
            hw.IsVisible = anyGroup;
        }
    }

    // ---------- panel ----------

    private void OnPanelToggled(SensorNode sensor)
    {
        if (sensor.IsInPanel)
        {
            if (Panel.All(p => p.Id != sensor.Id))
                AddPanelItem(new PanelItem(sensor.Id, sensor.DefaultLabel) { Sensor = sensor });
        }
        else
        {
            var item = Panel.FirstOrDefault(p => p.Id == sensor.Id);
            if (item is not null) RemovePanelItem(item);
        }
        ScheduleSave();
    }

    private void AddPanelItem(PanelItem item)
    {
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PanelItem.Label)) ScheduleSave(); };
        Panel.Add(item);
        OnPropertyChanged(nameof(IsPanelEmpty));
    }

    private void RemovePanelItem(PanelItem item)
    {
        Panel.Remove(item);
        OnPropertyChanged(nameof(IsPanelEmpty));
    }

    public void RemoveFromPanel(PanelItem item)
    {
        RemovePanelItem(item);
        item.Sensor?.SetInPanelSilently(false);
        ScheduleSave();
    }

    public void MovePanelItem(PanelItem item, int delta)
    {
        var i = Panel.IndexOf(item);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Panel.Count) return;
        Panel.Move(i, j);
        ScheduleSave();
    }

    /// <summary>Used by --snap test mode: put a few typical sensors on the panel.</summary>
    public void AddDemoSensors()
    {
        var picks = _sensors.Values.Where(s => s.Type == SensorType.Temperature && s.Value > 0).Take(4)
            .Concat(_sensors.Values.Where(s => s.Type == SensorType.Fan && s.Value > 0).Take(2))
            .Concat(_sensors.Values.Where(s => s.Type == SensorType.Load).Take(1));
        foreach (var s in picks) s.IsInPanel = true;
    }

    // ---------- persistence ----------

    private void ScheduleSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void SaveNow()
    {
        _settings.Panel = Panel.Select(p => new PanelEntry { Id = p.Id, Label = p.Label }).ToList();
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _saveDebounce.Stop();
        SaveNow();
    }
}
