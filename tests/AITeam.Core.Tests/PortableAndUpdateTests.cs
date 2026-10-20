using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class PortableAndUpdateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "aiteam-portable-" + Guid.NewGuid().ToString("N")[..8]);

    public PortableAndUpdateTests() => Directory.CreateDirectory(_tmp);

    // ---------- 家在哪裡 ----------

    [Fact]
    public void FolderWithData_IsHome_EvenWhenTheOldDDriveFolderExists()
    {
        var exeDir = Path.Combine(_tmp, "AITeam");
        var root = RuntimeRootResolver.Resolve(null, exeDir, dir =>
            dir == Path.Combine(exeDir, "data") || dir == RuntimeRootResolver.LegacyRoot);

        Assert.Equal(exeDir, root);
    }

    [Fact]
    public void FreshDownload_OnAMachineWithTheOldFolder_KeepsUsingIt()
    {
        var exeDir = Path.Combine(_tmp, "Downloads", "AITeam");
        var root = RuntimeRootResolver.Resolve(null, exeDir, dir => dir == RuntimeRootResolver.LegacyRoot);

        Assert.Equal(RuntimeRootResolver.LegacyRoot, root);
    }

    [Fact]
    public void FreshDownload_OnANewMachine_IsHomeItself()
    {
        var exeDir = Path.Combine(_tmp, "AITeam");

        Assert.Equal(exeDir, RuntimeRootResolver.Resolve(null, exeDir, _ => false));
    }

    // ---------- 專案路徑跟著家走 ----------

    [Fact]
    public void PathsInsideHome_AreStoredRelative_AndComeBackAbsolute()
    {
        var home = Path.Combine(_tmp, "home");
        var repo = Path.Combine(home, "repos", "CYapps");

        var stored = ProjectRegistryService.ToRelative(home, repo);
        Assert.False(Path.IsPathRooted(stored));
        Assert.Equal(repo, ProjectRegistryService.ToAbsolute(home, stored, _ => true));
    }

    [Fact]
    public void PathsOutsideHome_AreLeftAlone()
    {
        var elsewhere = Path.Combine(_tmp, "elsewhere", "repo");

        Assert.Equal(elsewhere, ProjectRegistryService.ToRelative(Path.Combine(_tmp, "home"), elsewhere));
    }

    [Fact]
    public void OldAbsolutePath_FromBeforeTheMove_IsFoundInTheNewHome()
    {
        var oldRepo = Path.Combine(_tmp, "old-home", "repos", "CYapps", "apps", "CYInvoice");
        var newHome = Path.Combine(_tmp, "new-home");
        var moved = Path.Combine(newHome, "repos", "CYapps", "apps", "CYInvoice");
        Directory.CreateDirectory(moved);

        Assert.Equal(moved, ProjectRegistryService.ToAbsolute(newHome, oldRepo, Directory.Exists));
    }

    [Fact]
    public void SavedRegistry_SurvivesMovingTheWholeFolder()
    {
        var oldHome = Path.Combine(_tmp, "old");
        var repo = Path.Combine(oldHome, "repos", "X");
        Directory.CreateDirectory(repo);
        new ProjectRegistryService(oldHome).Add(new AITeam.Models.ProjectEntry
        {
            Name = "X", RepoPath = repo, ProjectPath = repo, GitHubRepo = "o/X"
        });

        var newHome = Path.Combine(_tmp, "new");
        Directory.Move(oldHome, newHome);

        var loaded = Assert.Single(new ProjectRegistryService(newHome).Load());
        Assert.Equal(Path.Combine(newHome, "repos", "X"), loaded.RepoPath);
        Assert.True(Directory.Exists(loaded.PhysicalPath));
    }

    // ---------- 自動更新 ----------

    private static string Release(string tag, bool draft = false, bool pre = false, bool withAssets = true, string? zipName = null)
    {
        var version = tag.TrimStart('v');
        var zip = zipName ?? $"AITeam-V{version}.zip";
        var assets = withAssets
            ? $$"""[{"name":"{{zip}}","browser_download_url":"https://github.com/simonliu1118-byte/AITeam/releases/download/{{tag}}/{{zip}}"},{"name":"{{zip}}.sha256","browser_download_url":"https://github.com/simonliu1118-byte/AITeam/releases/download/{{tag}}/{{zip}}.sha256"}]"""
            : "[]";
        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"assets":{{assets}}}""";
    }

    [Fact]
    public void PicksTheNewestFormalRelease()
    {
        var json = $"[{Release("v0.33.0")},{Release("v0.35.0")},{Release("v0.34.0")}]";

        var offer = AppUpdateCheck.Newest(json, new Version(0, 33, 0));

        Assert.NotNull(offer);
        Assert.Equal(new Version(0, 35, 0), offer!.Version);
        Assert.Equal("AITeam-V0.35.0.zip", offer.ZipName);
        Assert.Equal("AITeam", offer.PackageFolder);
    }

    [Fact]
    public void IgnoresDraftsPrereleasesOlderAndIncompleteReleases()
    {
        var json = $"[{Release("v0.40.0", draft: true)},{Release("v0.39.0", pre: true)},{Release("v0.38.0", withAssets: false)},{Release("v0.32.0")},{Release("cyerp-auto-input-v9.0.0")}]";

        Assert.Null(AppUpdateCheck.Newest(json, new Version(0, 33, 0)));
    }

    [Fact]
    public void DownloadsOnlyFromThisRepositorysOwnRelease()
    {
        var json = """[{"tag_name":"v1.0.0","assets":[{"name":"AITeam-V1.0.0.zip","browser_download_url":"https://evil.example/AITeam-V1.0.0.zip"},{"name":"AITeam-V1.0.0.zip.sha256","browser_download_url":"https://evil.example/x.sha256"}]}]""";

        Assert.Null(AppUpdateCheck.Newest(json, new Version(0, 33, 0)));
    }

    [Fact]
    public void ReadsTheChecksumWrittenByTheReleaseWorkflow()
    {
        var hash = new string('a', 64);
        var text = $"{hash}  AITeam-V0.34.0.zip\r\n";

        Assert.Equal(hash, AppUpdateCheck.ExpectedSha256(text, "AITeam-V0.34.0.zip"));
        Assert.Null(AppUpdateCheck.ExpectedSha256(text, "other.zip"));
    }

    [Theory]
    [InlineData("0.33.0+build.0", "0.33.0")]
    [InlineData("0.33.0+build.2", "0.33.0")]
    [InlineData("0.33.0", "0.33.0")]
    public void CurrentVersion_IgnoresTheBuildNumber(string informational, string expected)
    {
        Assert.Equal(Version.Parse(expected), AppUpdateCheck.CurrentVersion(informational));
    }

    [Fact]
    public void UpdateScriptOutcome_IsParsed()
    {
        var ok = UpdateOutcome.Parse("﻿ok\r\n0.34.0\r\n");
        var failed = UpdateOutcome.Parse("failed\n0.34.0\n複製失敗：檔案被鎖住\n已還原原本的版本");

        Assert.True(ok!.Ok);
        Assert.Equal("0.34.0", ok.Version);
        Assert.False(failed!.Ok);
        Assert.Contains("已還原", failed.Detail);
        Assert.Null(UpdateOutcome.Parse("garbage"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }
}
