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
            Assert.Null(checker.Check(dir, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
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
            var issue = checker.Check(dir, 0);
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
            Assert.Null(checker.Check(dir, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
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
            Assert.Null(checker.Check(dir, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
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
            Assert.Null(checker.Check(dir, 0));
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
            Assert.Equal("no PBO files in addons folder", checker.Check(dir, 0));
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
            Assert.Equal("content directory missing or empty", checker.Check(dir, 0));
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
        Assert.Equal("content directory missing or empty", checker.Check(dir, 0));
    }

    [Fact]
    public void PboModifiedLongAfterBaselineReturnsModifiedIssue()
    {
        var dir = CreateTempDir();
        try
        {
            var addons = CreateAddons(dir);
            File.WriteAllText(Path.Combine(addons, "core.pbo"), "pbo");
            File.WriteAllText(Path.Combine(addons, "core.pbo.M6.bisign"), "sig");
            var baseline = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
            var checker = new ModIntegrityChecker();
            var issue = checker.Check(dir, baseline);
            Assert.NotNull(issue);
            Assert.StartsWith("modified after install", issue);
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
            var pbo = Path.Combine(addons, "core.pbo");
            var sig = Path.Combine(addons, "core.pbo.M6.bisign");
            File.WriteAllText(pbo, "pbo");
            File.WriteAllText(sig, "sig");
            var old = DateTime.UtcNow.AddDays(-20);
            File.SetLastWriteTimeUtc(pbo, old);
            File.SetLastWriteTimeUtc(sig, old);
            var keys = Directory.CreateDirectory(Path.Combine(dir, "Keys")).FullName;
            File.WriteAllText(Path.Combine(keys, "M6.bikey"), "key");
            var baseline = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
            var checker = new ModIntegrityChecker();
            var issue = checker.Check(dir, baseline);
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
            Assert.Null(checker.Check(dir, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }
}
