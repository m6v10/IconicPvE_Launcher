using Serilog.Core;
using Serilog.Events;

namespace IconicLauncher.Core.Utils;

public static class LogLevelController
{
    public static readonly LoggingLevelSwitch Switch = new(LogEventLevel.Information);

    public static void SetDebug(bool enabled)
    {
        Switch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    public static bool IsDebug => Switch.MinimumLevel <= LogEventLevel.Debug;
}
