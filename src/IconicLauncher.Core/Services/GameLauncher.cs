using System.Diagnostics;
using System.Text;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class GameLauncher : IGameLauncher
{
    private const string BePrefix = "0 1 1 -exe DayZ_x64.exe ";

    public LaunchResult Launch(ServerEntry server, IReadOnlyList<ModVerificationResult> mods, SteamEnvironment env, LauncherSettings settings, string? password = null)
    {
        if (env.DayZDir == null)
            return new LaunchResult { Started = false, Error = "DayZ installation not found" };
        var notReady = mods.Where(m => m.State != ModState.Ready || m.InstallPath == null).ToList();
        if (notReady.Count > 0)
            return new LaunchResult { Started = false, Error = "Mods not ready: " + string.Join(", ", notReady.Select(m => m.Mod.Name)) };

        var args = BuildArguments(server, mods.Select(m => m.InstallPath!), settings, password);
        var exe = Path.Combine(env.DayZDir, "DayZ_BE.exe");
        if (!File.Exists(exe))
        {
            Log.Warning("DayZ_BE.exe not found at {Path}, falling back to DayZ_x64.exe without BattlEye stub", exe);
            exe = Path.Combine(env.DayZDir, "DayZ_x64.exe");
            if (!File.Exists(exe))
                return new LaunchResult { Started = false, Error = "Neither DayZ_BE.exe nor DayZ_x64.exe found in " + env.DayZDir };
            if (args.StartsWith(BePrefix, StringComparison.Ordinal))
                args = args[BePrefix.Length..];
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = env.DayZDir,
                UseShellExecute = false
            };
            var process = Process.Start(startInfo);
            if (process == null)
                return new LaunchResult { Started = false, Error = "Process failed to start" };
            Log.Information("Launched {Exe} pid {Pid} for server {Server}", exe, process.Id, server.Name);
            return new LaunchResult { Started = true, ProcessId = process.Id };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch {Exe}", exe);
            return new LaunchResult { Started = false, Error = ex.Message };
        }
    }

    public static string BuildArguments(ServerEntry server, IEnumerable<string> modPaths, LauncherSettings settings, string? password = null)
    {
        var sb = new StringBuilder();
        sb.Append(BePrefix);
        if (settings.LaunchWindowed)
            sb.Append("-window ");
        if (settings.LaunchNoSplash)
            sb.Append("-noSplash ");
        if (settings.LaunchNoPause)
            sb.Append("-noPause ");
        if (settings.LaunchDoLogs)
            sb.Append("-doLogs ");
        var name = settings.ProfileName.Trim();
        if (name.Any(char.IsWhiteSpace))
            sb.Append("\"-name=").Append(name).Append('"');
        else
            sb.Append("-name=").Append(name);
        sb.Append(" \"").Append(ModListBuilder.BuildModArgument(modPaths)).Append('"');
        if (settings.AutoConnect)
            sb.Append(" -connect=").Append(server.Ip).Append(':').Append(server.GamePort).Append(':').Append(server.QueryPort);
        if (!string.IsNullOrWhiteSpace(password))
        {
            var pass = password.Trim();
            if (pass.Any(char.IsWhiteSpace))
                sb.Append(" \"-password=").Append(pass).Append('"');
            else
                sb.Append(" -password=").Append(pass);
        }
        if (!string.IsNullOrWhiteSpace(settings.ExtraLaunchParams))
            sb.Append(' ').Append(settings.ExtraLaunchParams.Trim());
        return sb.ToString();
    }
}
