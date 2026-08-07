using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class LogUploadRateLimitTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstUploadIsAllowed()
    {
        var settings = new LauncherSettings();
        Assert.Null(LogUploadService.CheckRateLimit(settings, Now));
    }

    [Fact]
    public void UploadWithinCooldownIsBlocked()
    {
        var settings = new LauncherSettings { LogUploadTimesUtc = new List<DateTime> { Now.AddMinutes(-2) } };
        var message = LogUploadService.CheckRateLimit(settings, Now);
        Assert.NotNull(message);
        Assert.Contains("wait", message);
    }

    [Fact]
    public void UploadAfterCooldownIsAllowed()
    {
        var settings = new LauncherSettings { LogUploadTimesUtc = new List<DateTime> { Now.AddMinutes(-6) } };
        Assert.Null(LogUploadService.CheckRateLimit(settings, Now));
    }

    [Fact]
    public void DailyCapBlocksSixthUpload()
    {
        var times = Enumerable.Range(1, 5).Select(h => Now.AddHours(-h)).ToList();
        var settings = new LauncherSettings { LogUploadTimesUtc = times };
        var message = LogUploadService.CheckRateLimit(settings, Now);
        Assert.NotNull(message);
        Assert.Contains("limit", message);
    }

    [Fact]
    public void EntriesOlderThanADayArePruned()
    {
        var times = Enumerable.Range(1, 5).Select(d => Now.AddHours(-25 - d)).ToList();
        var settings = new LauncherSettings { LogUploadTimesUtc = times };
        Assert.Null(LogUploadService.CheckRateLimit(settings, Now));
        Assert.Empty(settings.LogUploadTimesUtc);
    }

    [Fact]
    public void NullListIsTreatedAsEmpty()
    {
        var settings = new LauncherSettings { LogUploadTimesUtc = null! };
        Assert.Null(LogUploadService.CheckRateLimit(settings, Now));
    }

    [Fact]
    public void RecordUploadAppendsTimestamp()
    {
        var settings = new LauncherSettings();
        LogUploadService.RecordUpload(settings, Now);
        Assert.Single(settings.LogUploadTimesUtc);
        Assert.Equal(Now, settings.LogUploadTimesUtc[0]);
    }
}
