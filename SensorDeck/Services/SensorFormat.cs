using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace SensorDeck.Services;

public static class SensorFormat
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static string Format(SensorType type, float? value)
    {
        if (value is not float v || float.IsNaN(v)) return "—";
        return type switch
        {
            SensorType.Temperature => v.ToString("F0", Ci) + " °C",
            SensorType.Fan => v.ToString("F0", Ci) + " RPM",
            SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => v.ToString("F0", Ci) + " %",
            SensorType.Clock => v.ToString("F0", Ci) + " MHz",
            SensorType.Voltage => v.ToString("F3", Ci) + " V",
            SensorType.Current => v.ToString("F2", Ci) + " A",
            SensorType.Power => v.ToString("F1", Ci) + " W",
            SensorType.Data => v.ToString("F1", Ci) + " GB",
            SensorType.SmallData => v.ToString("F0", Ci) + " MB",
            SensorType.Throughput => FormatThroughput(v),
            SensorType.Frequency => v.ToString("F0", Ci) + " Hz",
            SensorType.Flow => v.ToString("F0", Ci) + " L/h",
            SensorType.Energy => v.ToString("F0", Ci) + " mWh",
            SensorType.Noise => v.ToString("F0", Ci) + " dBA",
            SensorType.TimeSpan => TimeSpan.FromSeconds(v).ToString(@"hh\:mm\:ss", Ci),
            _ => v.ToString("F2", Ci),
        };
    }

    private static string FormatThroughput(float bytesPerSec) => bytesPerSec switch
    {
        >= 1024 * 1024 => (bytesPerSec / 1024 / 1024).ToString("F1", Ci) + " MB/s",
        >= 1024 => (bytesPerSec / 1024).ToString("F0", Ci) + " KB/s",
        _ => bytesPerSec.ToString("F0", Ci) + " B/s",
    };

    public static string GroupTitle(SensorType type) => type switch
    {
        SensorType.Temperature => "Temperatures",
        SensorType.Fan => "Fans",
        SensorType.Control => "Fan control",
        SensorType.Load => "Load",
        SensorType.Clock => "Clocks",
        SensorType.Voltage => "Voltages",
        SensorType.Current => "Currents",
        SensorType.Power => "Power",
        SensorType.Data => "Data",
        SensorType.SmallData => "Memory",
        SensorType.Throughput => "Throughput",
        SensorType.Level => "Level",
        SensorType.Frequency => "Frequency",
        SensorType.Energy => "Energy",
        SensorType.Factor => "Factors",
        SensorType.TimeSpan => "Time",
        _ => type.ToString(),
    };

    /// <summary>Order of groups inside a hardware node: the most useful first.</summary>
    public static int GroupOrder(SensorType type) => type switch
    {
        SensorType.Temperature => 0,
        SensorType.Fan => 1,
        SensorType.Control => 2,
        SensorType.Load => 3,
        SensorType.Clock => 4,
        SensorType.Power => 5,
        SensorType.Voltage => 6,
        SensorType.Current => 7,
        _ => 10,
    };

    public static string ShortHardware(HardwareType type) => type switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
        HardwareType.Motherboard or HardwareType.SuperIO => "MB",
        HardwareType.Memory => "RAM",
        HardwareType.Storage => "Disk",
        HardwareType.Network => "Net",
        HardwareType.Cooler => "Cooler",
        HardwareType.Psu => "PSU",
        HardwareType.Battery => "Bat",
        HardwareType.EmbeddedController => "EC",
        _ => type.ToString(),
    };
}
