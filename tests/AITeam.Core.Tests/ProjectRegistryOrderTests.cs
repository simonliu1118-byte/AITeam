using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProjectRegistryOrderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiteam-registry-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void ProjectsKeepTheOrderInTheFile_NotAlphabeticalOrder()
    {
        var registry = new ProjectRegistryService(_root);
        registry.Save(Document("志遠企業管理系統", "CYAccounting", "刮刮樂"));

        Assert.Equal(new[] { "志遠企業管理系統", "CYAccounting", "刮刮樂" }, registry.Load().Select(p => p.Name));
    }

    [Fact]
    public void ReorderWritesTheNewOrder()
    {
        var registry = new ProjectRegistryService(_root);
        registry.Save(Document("A", "B", "C"));

        registry.Reorder(new[] { "C", "A", "B" });

        Assert.Equal(new[] { "C", "A", "B" }, registry.Load().Select(p => p.Name));
    }

    [Fact]
    public void ProjectsMissingFromTheNewOrder_AreKeptAtTheEnd()
    {
        var registry = new ProjectRegistryService(_root);
        var document = Document("A", "B", "C");
        document.Projects[2].Active = false; // 停用中的專案不會出現在畫面清單上
        registry.Save(document);

        registry.Reorder(new[] { "B", "A" });

        Assert.Equal(new[] { "B", "A", "C" }, registry.LoadDocument().Projects.Select(p => p.Name));
    }

    private static ProjectRegistryDocument Document(params string[] names) => new()
    {
        Projects = names.Select(n => new ProjectEntry { Name = n, GitHubRepo = "o/" + n }).ToList()
    };

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
