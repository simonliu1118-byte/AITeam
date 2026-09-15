using System.Text;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProcessRunnerStartInfoTests
{
    [Fact]
    public void RedirectedStdinIsUtf8_OrChinesePromptsArriveCorrupted()
    {
        // 沒有指定 StandardInputEncoding 時 .NET 會用系統 ANSI 代碼頁（繁中 Windows 是 CP950），
        // CLI 收到的中文就不是合法的 UTF-8，會以
        // 「input is not valid UTF-8 (invalid byte at offset N)」直接失敗。
        var psi = ProcessRunner.CreateStartInfo("codex", Array.Empty<string>(), ".", "議題：要不要換資料庫？");

        Assert.True(psi.RedirectStandardInput);
        Assert.NotNull(psi.StandardInputEncoding);
        Assert.Equal(Encoding.UTF8.CodePage, psi.StandardInputEncoding!.CodePage);
        // BOM 會被 CLI 當成內容的一部分，一定要關掉。
        Assert.Empty(psi.StandardInputEncoding.GetPreamble());
    }

    [Fact]
    public void WithoutStdin_NothingIsRedirected()
    {
        var psi = ProcessRunner.CreateStartInfo("claude", Array.Empty<string>(), ".", null);

        Assert.False(psi.RedirectStandardInput);
        Assert.Null(psi.StandardInputEncoding);
    }

    [Fact]
    public void OutputIsAlsoReadAsUtf8()
    {
        var psi = ProcessRunner.CreateStartInfo("codex", Array.Empty<string>(), ".", null);

        Assert.Equal(Encoding.UTF8.CodePage, psi.StandardOutputEncoding!.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, psi.StandardErrorEncoding!.CodePage);
    }

    [Fact]
    public void ArgumentsArePassedSeparately_SoSpacesAndQuotesCannotBreakTheCommand()
    {
        var psi = ProcessRunner.CreateStartInfo(
            "codex", new[] { "exec", "--output-last-message", @"C:\path with space\a.txt" }, ".", null);

        Assert.Equal(3, psi.ArgumentList.Count);
        Assert.Equal(@"C:\path with space\a.txt", psi.ArgumentList[2]);
    }

    [Fact]
    public void CliGetsItsOwnHiddenWindowByDefault()
    {
        // 預設行為＝現在已知穩定的做法。隱藏主控台是實驗性選項，沒有明講就不該生效。
        var psi = ProcessRunner.CreateStartInfo("codex", Array.Empty<string>(), ".", null);

        Assert.True(psi.CreateNoWindow);
    }

    [Fact]
    public void CliSharesOurHiddenConsole_WhenAskedTo()
    {
        // 關掉 CreateNoWindow，CLI（以及它叫起來的孫程序）才會沿用我們自己隱藏起來的主控台。
        var psi = ProcessRunner.CreateStartInfo("codex", Array.Empty<string>(), ".", null, inheritHiddenConsole: true);

        Assert.False(psi.CreateNoWindow);
    }
}
