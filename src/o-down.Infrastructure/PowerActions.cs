using System.Diagnostics;

namespace o_down.Infrastructure;

public static class PowerActions
{
    public static void Shutdown(int delaySeconds = 30, bool force = false)
    {
        var args = $"-s -t {delaySeconds}";
        if (force) args += " -f";
        Process.Start(new ProcessStartInfo("shutdown", args) { CreateNoWindow = true, UseShellExecute = false });
    }

    public static void Hibernate() =>
        Process.Start(new ProcessStartInfo("shutdown", "-h") { CreateNoWindow = true, UseShellExecute = false });

    public static void Sleep() =>
        Application.SetSuspendState(PowerState.Suspend, false, false);

    public static void Lock() =>
        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true, UseShellExecute = false });

    public static void Logout() =>
        Process.Start(new ProcessStartInfo("shutdown", "-l") { CreateNoWindow = true, UseShellExecute = false });

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}

internal enum PowerState
{
    Suspend = 0,
    Hibernate = 1
}

internal static class Application
{
    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static bool SetSuspendState(PowerState state, bool force, bool disableWakeEvent) =>
        SetSuspendState(state == PowerState.Hibernate, force, disableWakeEvent);
}
