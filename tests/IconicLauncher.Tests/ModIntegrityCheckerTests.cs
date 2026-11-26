using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class ModIntegrityCheckerTests
{
    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateAddons(string dir, string name = "addons")
    {
        return Directory.CreateDirectory(Path.Combine(dir, name)).FullName;
    }

    private static void DeleteTempDir(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void HealthyModReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void PboWithoutBisignInAddonsReturnsIssue()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir, "Addons");
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            var checker = new ModIntegrityChecker();
            var issue = checker.Check("1", dir, "m1");
            Assert.NotNull(issue);
            Assert.Contains("missing .bisign", issue);
            Assert.Contains("core.pbo", issue);
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void UnsignedPboOutsideAddonsIsIgnored()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var extras = Directory.CreateDirectory(Path.Combine(dir, "ServerFiles")).FullName;
            File.WriteAllText(Path.Combine(extras, "serveronly.pbo"), "pbo");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void UnsignedPboInAddonsSubfolderIsIgnored()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var nested = Directory.CreateDirectory(Path.Combine(addons, "IconicLootMod")).FullName;
            File.WriteAllText(Path.Combine(nested, "IconicLootMod_Server.pbo"), "pbo");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void ModWithoutAddonsFolderReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mod.cpp"), "name=\"test\";");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void EmptyAddonsFolderReturnsIssue()
    {
        var dir = CreateTempDir();
        try
        {
            CreateAddons(dir);
            var checker = new ModIntegrityChecker();
            Assert.Equal("no PBO files in addons folder", checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void EmptyDirectoryReturnsIssue()
    {
        var dir = CreateTempDir();
        try
        {
            var checker = new ModIntegrityChecker();
            Assert.Equal("content directory missing or empty", checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void MissingDirectoryReturnsIssue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N"));
        var checker = new ModIntegrityChecker();
        Assert.Equal("content directory missing or empty", checker.Check("1", dir, "m1"));
    }

    [Fact]
    public void FirstCheckOfAManifestTakesTheBaseline()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            var pbo = Path.Combine(addons, "core.pbo");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            File.SetLastWriteTimeUtc(pbo, DateTime.UtcNow.AddDays(30));
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void SteamUpdateWithNewManifestIsNotFlagged()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            var pbo = Path.Combine(addons, "core.pbo");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            File.WriteAllText(pbo, "pbo version two");
            File.SetLastWriteTimeUtc(pbo, DateTime.UtcNow.AddDays(3));
            Assert.Null(checker.Check("1", dir, "m2"));
            Assert.Null(checker.Check("1", dir, "m2"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void PboChangedWithoutManifestChangeReturnsModifiedIssue()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            var pbo = Path.Combine(addons, "core.pbo");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            File.WriteAllText(pbo, "tampered pbo");
            var issue = checker.Check("1", dir, "m1");
            Assert.NotNull(issue);
            Assert.StartsWith("modified after install", issue);
            Assert.Contains("core.pbo", issue);
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void ModifiedKeyInKeysFolderReturnsModifiedIssue()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var keys = Directory.CreateDirectory(Path.Combine(dir, "Keys")).FullName;
            var key = Path.Combine(keys, "M6.bikey");
            File.WriteAllText(key, "key");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            File.SetLastWriteTimeUtc(key, DateTime.UtcNow.AddHours(5));
            var issue = checker.Check("1", dir, "m1");
            Assert.NotNull(issue);
            Assert.StartsWith("modified after install", issue);
            Assert.Contains("M6.bikey", issue);
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void AddedAndRemovedFilesAreReported()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            File.WriteAllText(Path.Combine(addons, "extra.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "extra.pbo.M6.bisign"), "sig");
            Assert.StartsWith("file added after install", checker.Check("1", dir, "m1"));
            File.Delete(Path.Combine(addons, "extra.pbo"));
            File.Delete(Path.Combine(addons, "extra.pbo.M6.bisign"));
            File.Delete(Path.Combine(addons, "core.pbo.M6.bisign"));
            File.WriteAllText(Path.Combine(addons, "core.pbo.Other.bisign"), "sig");
            Assert.StartsWith("file removed after install", checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void ForgetTakesAFreshBaselineAfterAReinstall()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            var pbo = Path.Combine(addons, "core.pbo");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
            File.WriteAllText(pbo, "reinstalled pbo");
            checker.Forget("1");
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void BaselineSurvivesARestart()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            var pbo = Path.Combine(addons, "core.pbo");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var store = Path.Combine(dir, "baseline.json");
            Assert.Null(new ModIntegrityChecker(store).Check("1", dir, "m1"));
            Assert.True(File.Exists(store));
            File.WriteAllText(pbo, "tampered pbo");
            Assert.StartsWith("modified after install", new ModIntegrityChecker(store).Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void NoManifestSkipsTheModifiedCheck()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, null));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void BisignNamingVariantsAreAccepted()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "alpha.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "alpha.pbo.M6.bisign"), "sig");
            File.WriteAllText(Path.Combine(addons, "bravo.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "bravo.pbo.SomeKey.V3.bisign"), "sig");
            var checker = new ModIntegrityChecker();
            Assert.Null(checker.Check("1", dir, "m1"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }
}
