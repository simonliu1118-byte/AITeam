using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProjectScaffoldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiteam-scaffold-" + Guid.NewGuid().ToString("N")[..8]);

    public ProjectScaffoldTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void NewProject_GetsItsFolderAndVersionFile()
    {
        var folder = Path.Combine(_root, "tools", "erp-query");

        ChangeTaskService.PrepareProjectScaffold(folder, Project("tools/erp-query"), isNewProject: true, startingVersion: "0.1.0");

        Assert.True(Directory.Exists(folder));
        Assert.Equal("0.1.0", File.ReadAllText(Path.Combine(folder, "VERSION")).Trim());
    }

    [Fact]
    public void ExistingProjectWhoseFolderVanished_IsNotSilentlyRecreated()
    {
        var folder = Path.Combine(_root, "apps", "gone");

        Assert.Throws<DirectoryNotFoundException>(() =>
            ChangeTaskService.PrepareProjectScaffold(folder, Project("apps/gone"), isNewProject: false, startingVersion: "0.1.0"));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void ExistingVersionFile_IsNeverOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, "VERSION"), "2.4.1\n");

        ChangeTaskService.PrepareProjectScaffold(_root, Project(""), isNewProject: false, startingVersion: "0.1.0");

        Assert.Equal("2.4.1", File.ReadAllText(Path.Combine(_root, "VERSION")).Trim());
    }

    [Fact]
    public void NoStartingVersion_LeavesVersionAlone()
    {
        ChangeTaskService.PrepareProjectScaffold(_root, Project(""), isNewProject: false, startingVersion: null);

        Assert.False(File.Exists(Path.Combine(_root, "VERSION")));
    }

    private ProjectEntry Project(string subpath) => new() { Name = "X", RepoPath = _root, RepoSubpath = subpath };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
