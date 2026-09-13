using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ComposeWithNotesTests
{
    [Fact]
    public void WithoutNotes_ThePromptIsUntouched()
    {
        Assert.Equal("base prompt", ChangeTaskService.ComposeWithNotes("base prompt", Array.Empty<string>()));
    }

    [Fact]
    public void NotesAreAppendedAsNumberedUserInstructions()
    {
        var composed = ChangeTaskService.ComposeWithNotes("base prompt", new[] { "改成藍色", "補測試" });

        Assert.StartsWith("base prompt", composed);
        Assert.Contains("1. 改成藍色", composed);
        Assert.Contains("2. 補測試", composed);
        // 要講清楚這些話來自使用者、而且優先於前面衝突的敘述。
        Assert.Contains("from the user", composed);
        Assert.Contains("take priority", composed);
    }
}
