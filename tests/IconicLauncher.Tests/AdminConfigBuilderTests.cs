using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public sealed class AdminConfigBuilderTests : IDisposable
{
    private readonly string _dir;
    private readonly AdminConfigBuilder _builder = new();

    public AdminConfigBuilderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "IconicLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void AddModFolder(string folderName, string? publishedId, string? name)
    {
        var dir = Path.Combine(_dir, folderName);
        Directory.CreateDirectory(dir);
        if (publishedId == null)
        {
            return;
        }
        var content = $"protocol = 4;\npublishedid = {publishedId};\nname = \"{name}\";\ntimestamp = 123456789;\n";
        File.WriteAllText(Path.Combine(dir, "meta.cpp"), content);
    }

    private static LauncherConfig Template() => new()
    {
        SchemaVersion = 1,
        Servers = new List<ServerEntry>
        {
            new()
            {
                Id = "eu1",
                Name = "Iconic PvE - EU 1",
                Ip = "0.0.0.0",
                GamePort = 2302,
                QueryPort = 2303,
                Mods = new List<ModEntry>
                {
                    new() { WorkshopId = "333", Name = "Kept Mod" },
                    new() { WorkshopId = "999", Name = "Removed Mod" }
                }
            },
            new()
            {
                Id = "test",
                Name = "Iconic PvE - Test Server",
                Ip = "0.0.0.0",
                GamePort = 2702,
                QueryPort = 2703,
                Mods = new List<ModEntry>
                {
                    new() { WorkshopId = "777", Name = "Other Server Mod" }
                }
            }
        }
    };

    [Fact]
    public void DedupeByPublishedIdKeepsFirstOccurrence()
    {
        AddModFolder("@AAA Dup", "222", "Dup First");
        AddModFolder("@ZZZ Dup", "222", "Dup Second");
        var config = _builder.Build(_dir, Template(), "eu1");
        var eu1 = config.Servers.First(s => s.Id == "eu1");
        var dups = eu1.Mods.Where(m => m.WorkshopId == "222").ToList();
        Assert.Single(dups);
        Assert.Equal("Dup First", dups[0].Name);
    }

    [Fact]
    public void NonAtFoldersAreSkipped()
    {
        AddModFolder("NotAMod", "111", "Should Not Appear");
        AddModFolder("@Real", "444", "Real Mod");
        var config = _builder.Build(_dir, Template(), "eu1");
        var eu1 = config.Servers.First(s => s.Id == "eu1");
        Assert.DoesNotContain(eu1.Mods, m => m.WorkshopId == "111");
        Assert.Contains(eu1.Mods, m => m.WorkshopId == "444" && m.Name == "Real Mod");
    }

    [Fact]
    public void FoldersWithoutMetaCppAreSkipped()
    {
        AddModFolder("@NoMeta", null, null);
        AddModFolder("@Real", "444", "Real Mod");
        var config = _builder.Build(_dir, Template(), "eu1");
        var eu1 = config.Servers.First(s => s.Id == "eu1");
        Assert.Single(eu1.Mods);
        Assert.Equal("444", eu1.Mods[0].WorkshopId);
    }

    [Fact]
    public void ZeroPublishedIdFoldersAreSkipped()
    {
        AddModFolder("@ZeroId", "0", "Unpublished Local Mod");
        AddModFolder("@Real", "444", "Real Mod");
        var config = _builder.Build(_dir, Template(), "eu1");
        var eu1 = config.Servers.First(s => s.Id == "eu1");
        Assert.Single(eu1.Mods);
        Assert.Equal("444", eu1.Mods[0].WorkshopId);
    }

    [Fact]
    public void TemplateOrderPreservedAndNewModsAppendedSortedByName()
    {
        AddModFolder("@Existing Kept", "333", "Kept Mod");
        AddModFolder("@Alpha Pack", "111", "Alpha Pack");
        AddModFolder("@New Bee", "444", "Bee");
        AddModFolder("@New Ant", "555", "Ant");
        var config = _builder.Build(_dir, Template(), "eu1");
        var eu1 = config.Servers.First(s => s.Id == "eu1");
        Assert.Equal(new[] { "333", "111", "555", "444" }, eu1.Mods.Select(m => m.WorkshopId).ToArray());
        Assert.DoesNotContain(eu1.Mods, m => m.WorkshopId == "999");
    }

    [Fact]
    public void OtherServersInTemplateUntouched()
    {
        AddModFolder("@Real", "444", "Real Mod");
        var config = _builder.Build(_dir, Template(), "eu1");
        var test = config.Servers.First(s => s.Id == "test");
        Assert.Single(test.Mods);
        Assert.Equal("777", test.Mods[0].WorkshopId);
        Assert.Equal("Other Server Mod", test.Mods[0].Name);
    }

    [Fact]
    public void TemplateInstanceIsNotMutated()
    {
        AddModFolder("@Real", "444", "Real Mod");
        var template = Template();
        var config = _builder.Build(_dir, template, "eu1");
        Assert.Equal(2, template.Servers.First(s => s.Id == "eu1").Mods.Count);
        Assert.Null(template.GeneratedUtc);
        Assert.NotSame(template, config);
    }

    [Fact]
    public void GeneratedUtcIsSet()
    {
        var config = _builder.Build(_dir, Template(), "eu1");
        Assert.False(string.IsNullOrEmpty(config.GeneratedUtc));
        Assert.True(DateTime.TryParse(config.GeneratedUtc, out _));
    }
}
