using System.Security.Cryptography;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class WowPatchServiceTests
{
    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "IconicLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    [Theory]
    [InlineData("Data/patch-4.MPQ", null)]
    [InlineData("Interface/AddOns/WBMLite/WBMLite.lua", null)]
    [InlineData("Wow.exe", null)]
    [InlineData("", "empty path")]
    [InlineData("C:/Windows/evil.dll", "absolute path not allowed")]
    [InlineData("/etc/passwd", "absolute path not allowed")]
    [InlineData("\\\\server\\share", "absolute path not allowed")]
    [InlineData("Data/../../evil.exe", "path traversal not allowed")]
    [InlineData("Data//x", "empty path segment")]
    [InlineData("WTF/Config.wtf", "'WTF' is a protected folder")]
    [InlineData("Cache/WDB/x", "'Cache' is a protected folder")]
    public void ValidateRelativePath_enforces_the_rules(string path, string? expectedError)
    {
        var result = WowPatchService.ValidateRelativePath(path);
        if (expectedError is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Equal(expectedError, result);
        }
    }

    [Fact]
    public void Diff_flags_missing_modified_ok_and_obsolete()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            File.WriteAllText(Path.Combine(root, "Data", "patch-4.MPQ"), "correct content");
            File.WriteAllText(Path.Combine(root, "Data", "patch-5.MPQ"), "tampered");
            File.WriteAllText(Path.Combine(root, "Data", "patch-9.MPQ"), "old junk");
            var okHash = Sha256Of(Path.Combine(root, "Data", "patch-4.MPQ"));
            var manifest = new WowManifest
            {
                Build = "test-1",
                Files = new List<WowManifestFile>
                {
                    new() { Path = "Data/patch-4.MPQ", SizeBytes = new FileInfo(Path.Combine(root, "Data", "patch-4.MPQ")).Length, Sha256 = okHash },
                    new() { Path = "Data/patch-5.MPQ", SizeBytes = 999, Sha256 = "AB" },
                    new() { Path = "Interface/AddOns/X/X.toc", SizeBytes = 10, Sha256 = "CD" }
                },
                Delete = new List<string> { "Data/patch-9.MPQ", "Data/not-there.MPQ" }
            };

            var checks = WowPatchService.Diff(manifest, root);

            Assert.Equal(WowFileState.Ok, checks.Single(c => c.Path == "Data/patch-4.MPQ").State);
            Assert.Equal(WowFileState.Modified, checks.Single(c => c.Path == "Data/patch-5.MPQ").State);
            Assert.Equal(WowFileState.Missing, checks.Single(c => c.Path == "Interface/AddOns/X/X.toc").State);
            Assert.Equal(WowFileState.Obsolete, checks.Single(c => c.Path == "Data/patch-9.MPQ").State);
            Assert.DoesNotContain(checks, c => c.Path == "Data/not-there.MPQ");
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Fact]
    public void Diff_skips_manifest_entries_that_escape_or_touch_protected_folders()
    {
        var root = CreateTempDir();
        try
        {
            var manifest = new WowManifest
            {
                Files = new List<WowManifestFile>
                {
                    new() { Path = "../outside.txt", SizeBytes = 1, Sha256 = "AB" },
                    new() { Path = "WTF/Config.wtf", SizeBytes = 1, Sha256 = "AB" }
                }
            };

            var checks = WowPatchService.Diff(manifest, root);

            Assert.Empty(checks);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Fact]
    public void EnsureRealmlist_writes_every_locale_and_is_idempotent()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Data", "enUS"));
            Directory.CreateDirectory(Path.Combine(root, "Data", "deDE"));
            Directory.CreateDirectory(Path.Combine(root, "Data", "Textures"));
            File.WriteAllText(Path.Combine(root, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.com");
            var config = new WowConfig { Realmlist = "set realmlist 178.82.229.148" };
            var service = new WowPatchService(new HttpClient());

            var first = service.EnsureRealmlist(config, root);
            var second = service.EnsureRealmlist(config, root);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal("set realmlist 178.82.229.148", File.ReadAllText(Path.Combine(root, "Data", "enUS", "realmlist.wtf")).Trim());
            Assert.Equal("set realmlist 178.82.229.148", File.ReadAllText(Path.Combine(root, "Data", "deDE", "realmlist.wtf")).Trim());
            Assert.False(File.Exists(Path.Combine(root, "Data", "Textures", "realmlist.wtf")));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Fact]
    public void BuildFileUrl_escapes_segments_but_keeps_separators()
    {
        var url = WowPatchService.BuildFileUrl("https://example.com/wow/files/", "Interface/AddOns/My Addon/My Addon.toc");
        Assert.Equal("https://example.com/wow/files/Interface/AddOns/My%20Addon/My%20Addon.toc", url);
    }

    [Fact]
    public void IsValidClientRoot_requires_wow_exe_and_data()
    {
        var root = CreateTempDir();
        try
        {
            Assert.False(WowPatchService.IsValidClientRoot(root));
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            Assert.False(WowPatchService.IsValidClientRoot(root));
            File.WriteAllText(Path.Combine(root, "Wow.exe"), "x");
            Assert.True(WowPatchService.IsValidClientRoot(root));
            Assert.False(WowPatchService.IsValidClientRoot(null));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }
}
