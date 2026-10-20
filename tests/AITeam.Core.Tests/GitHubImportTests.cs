using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class GitHubImportTests
{
    [Fact]
    public void RepoLines_AreParsed_AndArchivedOrBrokenLinesSkipped()
    {
        var output = string.Join("\n",
            """{"full_name":"me/CYapps","description":"公司小工具","private":true,"archived":false}""",
            """{"full_name":"me/old","description":null,"private":false,"archived":true}""",
            "not json",
            """{"full_name":"me/AITeam","description":null,"private":false,"archived":false}""");

        var repos = GitHubImportService.ParseRepositoryLines(output);

        Assert.Equal(new[] { "me/CYapps", "me/AITeam" }, repos.Select(r => r.FullName));
        Assert.True(repos[0].IsPrivate);
        Assert.Equal("公司小工具", repos[0].Description);
        Assert.Equal("", repos[1].Description);
    }

    [Fact]
    public void FoldersWithVersionFile_AreProjects()
    {
        var files = new[]
        {
            "README.md", "apps/AutoInput/VERSION", "apps/AutoInput/src/a.cs",
            "apps/Web/VERSION", ".github/workflows/VERSION", "apps/Web/docs/VERSION.md"
        };

        Assert.Equal(new[] { "apps/AutoInput", "apps/Web" }, GitHubImportService.FindProjectFolders(files));
    }

    [Fact]
    public void RootVersion_IsTheWholeRepo()
    {
        Assert.Equal(new[] { "" }, GitHubImportService.FindProjectFolders(new[] { "VERSION", "src/a.cs" }));
    }

    [Fact]
    public void RepoWithoutAnyVersion_IsOneProject()
    {
        Assert.Equal(new[] { "" }, GitHubImportService.FindProjectFolders(new[] { "README.md", "index.html" }));
    }

    [Fact]
    public void ReadmeSummary_IsTheFirstRealParagraph()
    {
        var readme = "# AutoInput\r\n\r\n[![CI](x)](y)\r\n\r\n把 ERP 資料自動輸入，\r\n**每天**省下人工。\r\n\r\n## 安裝\r\n其他內容";

        Assert.Equal("把 ERP 資料自動輸入， 每天省下人工。", GitHubImportService.SummarizeReadme(readme));
    }

    [Fact]
    public void LongReadmeSummary_StopsAtASentenceEnd_WithoutEllipsis()
    {
        var sentence = new string('字', 150) + "。";
        var summary = GitHubImportService.SummarizeReadme(sentence + sentence + sentence);

        Assert.Equal(sentence + sentence, summary);
        Assert.DoesNotContain("…", summary);
    }

    [Fact]
    public void DuplicateNames_GetTheRepoNameAdded()
    {
        Assert.Equal("Web", GitHubImportService.UniqueName("Web", "me/CYapps", new[] { "AITeam" }));
        Assert.Equal("Web（CYapps）", GitHubImportService.UniqueName("Web", "me/CYapps", new[] { "web" }));
        Assert.Equal("Web（CYapps） 2", GitHubImportService.UniqueName("Web", "me/CYapps", new[] { "Web", "Web（CYapps）" }));
    }

    [Fact]
    public void Candidate_BecomesAProjectInsideTheRepo()
    {
        var root = Path.Combine("home", "repos", "CYapps");
        var entry = GitHubImportService.ToProjectEntry(
            new ImportCandidate("Web", "me/CYapps", root, "apps/Web", "main", "說明"), "Web");

        Assert.Equal(Path.Combine(root, "apps", "Web"), entry.ProjectPath);
        Assert.Equal("CYapps", entry.RepoName);
        Assert.Equal("apps/Web", entry.RepoSubpath);
        Assert.Equal("說明", entry.Description);
        Assert.Null(entry.PendingCreation);
    }
}
