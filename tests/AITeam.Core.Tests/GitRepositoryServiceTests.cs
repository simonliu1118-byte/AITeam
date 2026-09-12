using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class GitRepositoryServiceBranchTests
{
    [Fact]
    public async Task PrefersOriginHead_OverPreferredBranch()
    {
        var runner = new FakeProcessRunner(args =>
        {
            if (Matches(args, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"))
                return Ok("origin/develop\n");
            throw Unexpected(args);
        });

        var git = new GitRepositoryService("unused-root", runner);
        var branch = await git.GetRemoteDefaultBranchAsync("repo-path", "main", CancellationToken.None);

        Assert.Equal("develop", branch);
    }

    [Fact]
    public async Task FallsBackToPreferredBranch_WhenOriginHeadUnavailable()
    {
        var runner = new FakeProcessRunner(args =>
        {
            if (Matches(args, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"))
                return Fail();
            if (Matches(args, "rev-parse", "--verify", "refs/remotes/origin/release"))
                return Ok("");
            throw Unexpected(args);
        });

        var git = new GitRepositoryService("unused-root", runner);
        var branch = await git.GetRemoteDefaultBranchAsync("repo-path", "release", CancellationToken.None);

        Assert.Equal("release", branch);
    }

    [Fact]
    public async Task FallsBackToMainGuess_WhenOriginHeadAndPreferredBranchUnavailable()
    {
        var runner = new FakeProcessRunner(args =>
        {
            if (Matches(args, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"))
                return Fail();
            if (Matches(args, "rev-parse", "--verify", "refs/remotes/origin/release"))
                return Fail();
            if (Matches(args, "rev-parse", "--verify", "refs/remotes/origin/main"))
                return Ok("");
            throw Unexpected(args);
        });

        var git = new GitRepositoryService("unused-root", runner);
        var branch = await git.GetRemoteDefaultBranchAsync("repo-path", "release", CancellationToken.None);

        Assert.Equal("main", branch);
    }

    [Fact]
    public async Task FallsBackToMasterGuess_WhenNoPreferredBranchGiven()
    {
        var runner = new FakeProcessRunner(args =>
        {
            if (Matches(args, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"))
                return Fail();
            if (Matches(args, "rev-parse", "--verify", "refs/remotes/origin/main"))
                return Fail();
            if (Matches(args, "rev-parse", "--verify", "refs/remotes/origin/master"))
                return Ok("");
            throw Unexpected(args);
        });

        var git = new GitRepositoryService("unused-root", runner);
        var branch = await git.GetRemoteDefaultBranchAsync("repo-path", null, CancellationToken.None);

        Assert.Equal("master", branch);
    }

    [Fact]
    public async Task Throws_WhenNothingResolves()
    {
        var runner = new FakeProcessRunner(_ => Fail());
        var git = new GitRepositoryService("unused-root", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => git.GetRemoteDefaultBranchAsync("repo-path", null, CancellationToken.None));
    }

    private static bool Matches(IReadOnlyList<string> args, params string[] expected) =>
        args.SequenceEqual(expected);

    private static ProcessRunResult Ok(string output) => new(0, output, "", TimeSpan.Zero);
    private static ProcessRunResult Fail() => new(1, "", "not found", TimeSpan.Zero);

    private static InvalidOperationException Unexpected(IReadOnlyList<string> args) =>
        new("unexpected git invocation: " + string.Join(' ', args));
}

public sealed class GitRepositoryServiceHelperTests
{
    [Theory]
    [InlineData("https://github.com/simonliu1118-byte/AITeam.git", "simonliu1118-byte/AITeam", true)]
    [InlineData("https://github.com/simonliu1118-byte/AITeam", "simonliu1118-byte/AITeam", true)]
    [InlineData("git@github.com:simonliu1118-byte/AITeam.git", "simonliu1118-byte/AITeam", true)]
    [InlineData("https://github.com/other/repo.git", "simonliu1118-byte/AITeam", false)]
    [InlineData("", "simonliu1118-byte/AITeam", false)]
    public void RemoteMatches_ComparesNormalizedUrl(string remote, string githubRepo, bool expected)
    {
        Assert.Equal(expected, GitRepositoryService.RemoteMatches(remote, githubRepo));
    }

    [Theory]
    [InlineData("simonliu1118-byte/AITeam", "simonliu1118-byte/AITeam")]
    [InlineData("  simonliu1118-byte/AITeam  ", "simonliu1118-byte/AITeam")]
    public void NormalizeGitHubRepo_TrimsValidInput(string input, string expected)
    {
        Assert.Equal(expected, GitRepositoryService.NormalizeGitHubRepo(input));
    }

    [Theory]
    [InlineData("not-a-repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("")]
    public void NormalizeGitHubRepo_RejectsInvalidInput(string input)
    {
        Assert.Throws<InvalidOperationException>(() => GitRepositoryService.NormalizeGitHubRepo(input));
    }
}
