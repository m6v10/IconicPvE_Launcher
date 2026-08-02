using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class WorkshopAcfTests
{
    private const string SampleAcf = """
        "AppWorkshop"
        {
            "appid"        "221100"
            "SizeOnDisk"        "81633164452"
            "NeedsUpdate"        "0"
            "NeedsDownload"        "0"
            "TimeLastUpdated"        "1753912345"
            "TimeLastAppRan"        "1753912999"
            "WorkshopItemsInstalled"
            {
                "1559212036"
                {
                    "size"        "3513771"
                    "timeupdated"        "1741604387"
                    "manifest"        "3190999023032234255"
                }
                "3077736647"
                {
                    "size"        "2097152"
                    "timeupdated"        "1751111111"
                    "manifest"        "8887776665554443332"
                }
            }
            "WorkshopItemDetails"
            {
                "1559212036"
                {
                    "manifest"        "3190999023032234255"
                    "timeupdated"        "1741604387"
                    "timetouched"        "1753000000"
                    "subscribed"        "1"
                    "latest_timeupdated"        "1741604387"
                    "latest_manifest"        "3190999023032234255"
                }
                "3077736647"
                {
                    "manifest"        "8887776665554443332"
                    "timeupdated"        "1751111111"
                    "timetouched"        "1753100000"
                    "subscribed"        "1"
                    "latest_timeupdated"        "1752222222"
                    "latest_manifest"        "9998887776665554443"
                }
            }
        }
        """;

    [Fact]
    public void ParseReadsInstalledItems()
    {
        var acf = WorkshopAcf.Parse(SampleAcf);
        Assert.Equal(2, acf.Installed.Count);
        var cf = acf.Installed["1559212036"];
        Assert.Equal("3190999023032234255", cf.Manifest);
        Assert.Equal(1741604387, cf.TimeUpdated);
        Assert.Equal(3513771, cf.Size);
        var core = acf.Installed["3077736647"];
        Assert.Equal("8887776665554443332", core.Manifest);
        Assert.Equal(1751111111, core.TimeUpdated);
        Assert.Equal(2097152, core.Size);
    }

    [Fact]
    public void ParseReadsDetails()
    {
        var acf = WorkshopAcf.Parse(SampleAcf);
        Assert.Equal(2, acf.Details.Count);
        var upToDate = acf.Details["1559212036"];
        Assert.Equal("3190999023032234255", upToDate.Manifest);
        Assert.Equal("3190999023032234255", upToDate.LatestManifest);
        Assert.Equal(1741604387, upToDate.TimeUpdated);
        Assert.Equal(1741604387, upToDate.LatestTimeUpdated);
        var outdated = acf.Details["3077736647"];
        Assert.Equal("8887776665554443332", outdated.Manifest);
        Assert.Equal("9998887776665554443", outdated.LatestManifest);
        Assert.Equal(1751111111, outdated.TimeUpdated);
        Assert.Equal(1752222222, outdated.LatestTimeUpdated);
    }

    [Fact]
    public void ParsedManifestsDifferOnlyForOutdatedItem()
    {
        var acf = WorkshopAcf.Parse(SampleAcf);
        Assert.Equal(acf.Details["1559212036"].LatestManifest, acf.Installed["1559212036"].Manifest);
        Assert.NotEqual(acf.Details["3077736647"].LatestManifest, acf.Installed["3077736647"].Manifest);
    }

    [Fact]
    public void LoadReadsValidFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N") + ".acf");
        try
        {
            File.WriteAllText(path, SampleAcf);
            var acf = WorkshopAcf.Load(path);
            Assert.NotNull(acf);
            Assert.Equal(2, acf.Installed.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadWithMalformedContentReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N") + ".acf");
        try
        {
            File.WriteAllText(path, "this is not vdf at all {{{ \"unclosed");
            var acf = WorkshopAcf.Load(path);
            Assert.Null(acf);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadWithMissingFileReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N") + ".acf");
        Assert.Null(WorkshopAcf.Load(path));
    }
}
