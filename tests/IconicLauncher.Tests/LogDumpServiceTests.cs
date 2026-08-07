using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class LogDumpServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "iconic-logdump-tests-" + Guid.NewGuid().ToString("N"));

    public LogDumpServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void DumpStartsWithMagicHeaderAndIncludesMetadata()
    {
        var settings = new LauncherSettings { ProfileName = "TestPlayer", DebugLogging = true };
        var dump = LogDumpService.BuildDump(_dir, "v1.0.4", settings, new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc));
        Assert.StartsWith(LogDumpService.DumpHeaderMagic, dump);
        Assert.Contains("launcher version: v1.0.4", dump);
        Assert.Contains("profile name: TestPlayer", dump);
        Assert.Contains("debug logging: True", dump);
    }

    [Fact]
    public void DumpIncludesLogFileContent()
    {
        File.WriteAllText(Path.Combine(_dir, "launcher-20260803.log"), "hello from the log");
        var dump = LogDumpService.BuildDump(_dir, "v1", new LauncherSettings(), DateTime.UtcNow);
        Assert.Contains("launcher-20260803.log", dump);
        Assert.Contains("hello from the log", dump);
    }

    [Fact]
    public void DumpTakesNewestThreeFilesOnly()
    {
        for (var day = 1; day <= 5; day++)
            File.WriteAllText(Path.Combine(_dir, $"launcher-2026080{day}.log"), $"day {day}");
        var dump = LogDumpService.BuildDump(_dir, "v1", new LauncherSettings(), DateTime.UtcNow);
        Assert.DoesNotContain("day 1", dump);
        Assert.DoesNotContain("day 2", dump);
        Assert.Contains("day 3", dump);
        Assert.Contains("day 4", dump);
        Assert.Contains("day 5", dump);
    }

    [Fact]
    public void OversizedLogsAreTailTruncatedKeepingTheNewestFile()
    {
        var oldContent = new string('a', 700_000) + "OLD-TAIL";
        var newContent = new string('b', 700_000) + "NEW-TAIL";
        File.WriteAllText(Path.Combine(_dir, "launcher-20260801.log"), oldContent);
        File.WriteAllText(Path.Combine(_dir, "launcher-20260802.log"), newContent);
        var dump = LogDumpService.BuildDump(_dir, "v1", new LauncherSettings(), DateTime.UtcNow);
        Assert.True(dump.Length <= LogDumpService.MaxDumpBytes + 2000, $"dump length {dump.Length}");
        Assert.Contains("NEW-TAIL", dump);
        Assert.Contains("truncated", dump);
        Assert.EndsWith("OLD-TAIL", ExtractSection(dump, "launcher-20260801.log"));
    }

    [Fact]
    public void MissingLogDirProducesHeaderOnlyDump()
    {
        var dump = LogDumpService.BuildDump(Path.Combine(_dir, "does-not-exist"), "v1", new LauncherSettings(), DateTime.UtcNow);
        Assert.StartsWith(LogDumpService.DumpHeaderMagic, dump);
        Assert.Contains("no log files found", dump);
    }

    [Fact]
    public void LockedLogFileCanStillBeRead()
    {
        var path = Path.Combine(_dir, "launcher-20260803.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes("still readable while locked");
        writer.Write(bytes);
        writer.Flush();
        var dump = LogDumpService.BuildDump(_dir, "v1", new LauncherSettings(), DateTime.UtcNow);
        Assert.Contains("still readable while locked", dump);
    }

    [Fact]
    public void DumpFileNameMatchesRequestedPattern()
    {
        var name = LogDumpService.DumpFileName(new DateTime(2026, 8, 3, 14, 5, 9));
        Assert.Equal("IconicPvE_Launcher_Logdump_2026-08-03_14-05-09.log", name);
    }

    private static string ExtractSection(string dump, string fileName)
    {
        var start = dump.IndexOf($"----- {fileName} -----", StringComparison.Ordinal);
        Assert.True(start >= 0, $"section {fileName} missing");
        start = dump.IndexOf('\n', start) + 1;
        var end = dump.IndexOf("----- ", start, StringComparison.Ordinal);
        if (end < 0)
            end = dump.Length;
        return dump[start..end].TrimEnd();
    }
}
