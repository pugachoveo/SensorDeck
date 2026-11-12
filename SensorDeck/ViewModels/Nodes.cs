using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;
using SensorDeck.Services;

namespace SensorDeck.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Common base for tree rows: expand state and search visibility.</summary>
public abstract class TreeNode : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isVisible = true;

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

public sealed class HardwareNode : TreeNode
{
    public HardwareNode(string name, HardwareType type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public HardwareType Type { get; }
    public string Badge => SensorFormat.ShortHardware(Type);
    public ObservableCollection<GroupNode> Groups { get; } = new();
}

public sealed class GroupNode : TreeNode
{
    public GroupNode(SensorType type) => Type = type;

    public SensorType Type { get; }
    public string Title => SensorFormat.GroupTitle(Type);
    public ObservableCollection<SensorNode> Sensors { get; } = new();
}

public sealed class SensorNode : TreeNode
{
    private string _valueText = "—", _minText = "—", _maxText = "—";
    private bool _isInPanel;

    public SensorNode(ISensor sensor, string hardwareName, string id)
    {
        Sensor = sensor;
        Id = id;
        HardwareName = hardwareName;
    }

    public ISensor Sensor { get; }
    public string Id { get; }
    public string Name => Sensor.Name;
    public string HardwareName { get; }
    public SensorType Type => Sensor.SensorType;
    public float? Value { get; private set; }

    public string DefaultLabel
    {
        get
        {
            var prefix = SensorFormat.ShortHardware(Sensor.Hardware.HardwareType);
            // Avoid "GPU GPU Core" / "CPU CPU Total".
            return Sensor.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? Sensor.Name
                : $"{prefix} {Sensor.Name}";
        }
    }

    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
    public string MinText { get => _minText; private set => Set(ref _minText, value); }
    public string MaxText { get => _maxText; private set => Set(ref _maxText, value); }

    /// <summary>Bound to the "+" toggle. The main view model reacts to changes.</summary>
    public bool IsInPanel
    {
        get => _isInPanel;
        set { if (Set(ref _isInPanel, value)) PanelToggled?.Invoke(this); }
    }

    /// <summary>Set without raising PanelToggled (used when restoring state).</summary>
    public void SetInPanelSilently(bool value) => Set(ref _isInPanel, value, nameof(IsInPanel));

    public event Action<SensorNode>? PanelToggled;

    public void Refresh()
    {
        Value = Sensor.Value;
        ValueText = SensorFormat.Format(Type, Sensor.Value);
        MinText = SensorFormat.Format(Type, Sensor.Min);
        MaxText = SensorFormat.Format(Type, Sensor.Max);
    }
}

public enum Severity { Normal, Warm, Hot }

/// <summary>A row on the overlay panel.</summary>
public sealed class PanelItem : ObservableObject
{
    private string _label;
    private SensorNode? _sensor;

    public PanelItem(string id, string label)
    {
        Id = id;
        _label = label;
    }

    public string Id { get; }

    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Null when the sensor is currently not present (e.g. driver not loaded).</summary>
    public SensorNode? Sensor
    {
        get => _sensor;
        set { if (Set(ref _sensor, value)) Refresh(); }
    }

    public string ValueText => Sensor?.ValueText ?? "n/a";

    public Severity Severity
    {
        get
        {
            if (Sensor is not { Type: SensorType.Temperature, Value: float t }) return Severity.Normal;
            return t >= 85 ? Severity.Hot : t >= 70 ? Severity.Warm : Severity.Normal;
        }
    }

    public string Source => Sensor is null ? "sensor not found" : $"{Sensor.HardwareName} · {Sensor.Name}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(Source));
    }
}
