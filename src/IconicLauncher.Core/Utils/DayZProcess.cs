using System.Diagnostics;

namespace IconicLauncher.Core.Utils;

public static class DayZProcess
{
    public static bool IsRunning()
    {
        try
        {
            return Process.GetProcessesByName("DayZ_x64").Length > 0 || Process.GetProcessesByName("DayZ_BE").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool ForceClose()
    {
        var ok = true;
        foreach (var name in new[] { "DayZ_x64", "DayZ_BE" })
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
