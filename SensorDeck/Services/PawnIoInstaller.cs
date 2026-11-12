using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace SensorDeck.Services;

public enum PawnIoState { NotInstalled, Outdated, Ok }

/// <summary>
/// Checks for the PawnIO kernel driver and installs the copy bundled inside SensorDeck.exe
/// (the same approach LibreHardwareMonitor itself uses).
/// </summary>
public static class PawnIoInstaller
{
    private const string ResourceName = "SensorDeck.PawnIO_setup.exe";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>Version of the installer embedded in the exe.</summary>
    public static readonly Version BundledVersion = new(2, 2, 0, 0);

    /// <summary>Oldest driver the bundled LibreHardwareMonitorLib modules work with.</summary>
    public static readonly Version MinimumVersion = new(2, 0, 0, 0);

    /// <summary>Read fresh from the registry every time (the library caches its own value at startup).</summary>
    public static Version? InstalledVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(UninstallKey);
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var v)) return v;
        }
        return null;
    }

    public static PawnIoState GetState()
    {
        var v = InstalledVersion();
        if (v is null) return PawnIoState.NotInstalled;
        return v < MinimumVersion ? PawnIoState.Outdated : PawnIoState.Ok;
    }

    public sealed record Result(bool Success, bool RebootRequired, string Message);

    public static async Task<Result> InstallAsync()
    {
        // Fresh random folder so nothing pre-planted can be picked up instead of our file.
        var dir = Path.Combine(Path.GetTempPath(), "SensorDeck-" + Guid.NewGuid().ToString("N"));
        var setup = Path.Combine(dir, "PawnIO_setup.exe");
        try
        {
            Directory.CreateDirectory(dir);
            await using (var res = typeof(PawnIoInstaller).Assembly.GetManifestResourceStream(ResourceName)
                                   ?? throw new InvalidOperationException("PawnIO installer is missing from the app"))
            await using (var file = File.Create(setup))
            {
                await res.CopyToAsync(file);
            }

            var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            var psi = isAdmin
                ? new ProcessStartInfo(setup, "-install -silent") { UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo(setup, "-install -silent") { UseShellExecute = true, Verb = "runas" };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the installer");
            await process.WaitForExitAsync();

            return process.ExitCode switch
            {
                0 => new Result(true, false, $"PawnIO {BundledVersion.ToString(3)} installed"),
                3010 => new Result(true, true, "PawnIO installed, Windows restart required"),
                var code => new Result(false, false, $"PawnIO installer exited with code {code}"),
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new Result(false, false, "Installation cancelled");
        }
        catch (Exception ex)
        {
            return new Result(false, false, "Could not install PawnIO: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp folder, not critical */ }
        }
    }
}
