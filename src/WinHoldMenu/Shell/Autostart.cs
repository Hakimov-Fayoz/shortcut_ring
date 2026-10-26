using Microsoft.Win32;

namespace WinHoldMenu.Shell;

internal static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "WinHoldMenu";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return runKey?.GetValue(EntryName) is not null;
        }
    }

    public static void Set(bool enabled)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled && Environment.ProcessPath is string exePath)
            runKey.SetValue(EntryName, $"\"{exePath}\"");
        else
            runKey.DeleteValue(EntryName, throwOnMissingValue: false);
    }

    public static void UpdatePathIfEnabled()
    {
        if (IsEnabled) Set(true);
    }
}
