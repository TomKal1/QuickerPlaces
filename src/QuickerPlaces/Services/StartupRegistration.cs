using System;
using System.IO;
using Microsoft.Win32;

namespace QuickerPlaces.Services;

/// <summary>The per-user Windows sign-in entry for Phase 9's opt-in startup.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool TryApply(bool enabled, out string? error)
    {
        error = null;
        try
        {
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable) ||
                    !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Windows startup needs the QuickerPlaces executable path.";
                    return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key is null)
                {
                    error = "Windows couldn't open your startup settings.";
                    return false;
                }
                key.SetValue(AppInfo.Name, $"\"{executable}\" --tray", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Windows startup registration failed ({ex.GetType().Name}: 0x{ex.HResult:X8}).");
            error = "Windows couldn't change the Start with Windows setting.";
            return false;
        }
    }
}
