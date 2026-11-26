using System.Diagnostics;

namespace IconicLauncher.Core.Utils;

public static class WowProcess
{
    private static readonly string[] Names = { "Wow", "Wow-64", "WoW" };

    public static bool IsRunning()
    {
        try
        {
            foreach (var name in Names)
            {
                if (Process.GetProcessesByName(name).Length > 0) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool ForceClose()
    {
        var ok = true;
        foreach (var name in Names)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                ok = false;
                continue;
            }
            foreach (var process in processes)
            {
                try
                {
                    if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    ok = false;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return ok && !IsRunning();
    }
}
