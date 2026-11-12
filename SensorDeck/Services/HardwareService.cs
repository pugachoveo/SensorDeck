using LibreHardwareMonitor.Hardware;

namespace SensorDeck.Services;

/// <summary>Wraps LibreHardwareMonitor: opens all hardware groups and refreshes sensor values.</summary>
public sealed class HardwareService : IDisposable
{
    private Computer _computer = CreateComputer();

    private readonly object _sync = new();
    private bool _opened;

    private static Computer CreateComputer() => new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsBatteryEnabled = true,
        IsPsuEnabled = true,
        IsPowerMonitorEnabled = true,
    };

    public void Open()
    {
        lock (_sync)
        {
            _computer.Open();
            _opened = true;
        }
    }

    /// <summary>
    /// Re-detect all hardware from scratch. Used right after the PawnIO driver is installed, so CPU and
    /// motherboard sensors appear without restarting the app.
    /// </summary>
    public void Reopen()
    {
        lock (_sync)
        {
            if (_opened) _computer.Close();
            _computer = CreateComputer();
            _computer.Open();
            _computer.Accept(new UpdateVisitor());
            _opened = true;
        }
    }

    public void Update()
    {
        lock (_sync)
        {
            if (_opened) _computer.Accept(new UpdateVisitor());
        }
    }

    /// <summary>All hardware including sub-hardware (e.g. the Super I/O chip under the motherboard), flattened.</summary>
    public List<(IHardware Hardware, string DisplayName)> GetAllHardware()
    {
        var result = new List<(IHardware, string)>();
        lock (_sync)
        {
            foreach (var hw in _computer.Hardware) Walk(hw, null, result);
        }
        return result;
    }

    private static void Walk(IHardware hw, string? parent, List<(IHardware, string)> acc)
    {
        var name = parent is null ? hw.Name : $"{parent} › {hw.Name}";
        acc.Add((hw, name));
        foreach (var sub in hw.SubHardware) Walk(sub, hw.Name, acc);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_opened) _computer.Close();
            _opened = false;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
