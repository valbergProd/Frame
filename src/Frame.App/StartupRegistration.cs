using Microsoft.Win32;

namespace Frame.App;

// "Start with Windows": a per-user Run entry (no admin, no scheduled task, no service). The command starts FRAME
// hidden in the tray with --background. Rewritten at each start while enabled, so a moved portable folder self-heals.
static class StartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "FRAME";
    public static string Command(string exe) => $"\"{exe}\" --background";
    public static bool IsEnabled(string key = RunKey)
    {
        using var run = Registry.CurrentUser.OpenSubKey(key);
        return run?.GetValue(ValueName) is string;
    }
    public static void Apply(bool enable, string exe, string key = RunKey)
    {
        using var run = Registry.CurrentUser.CreateSubKey(key, true) ?? throw new InvalidOperationException("Windows start-up settings could not be opened.");
        if (enable) { if (run.GetValue(ValueName) as string != Command(exe)) run.SetValue(ValueName, Command(exe), RegistryValueKind.String); }
        else if (run.GetValue(ValueName) != null) run.DeleteValue(ValueName, false);
    }
}
