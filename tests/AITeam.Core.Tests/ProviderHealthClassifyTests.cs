using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProviderHealthClassifyTests
{
    [Theory]
    // 給人看的句子
    [InlineData("Claude AI usage limit reached|1747958400")]
    [InlineData("You have reached your limit. Try again at 3pm.")]
    [InlineData("5-hour limit reached ∙ resets at 15:00")]
    // API 的機器代碼：底線／連字號寫法要能一起判到
    [InlineData("{\"error\":{\"type\":\"rate_limit_error\"}}")]
    [InlineData("{\"error\":{\"code\":\"RESOURCE_EXHAUSTED\"}}")]
    [InlineData("API Error: 429 Too Many Requests")]
    [InlineData("quota exceeded for this model")]
    public void QuotaMessages_AreClassifiedAsQuota_NotError(string message)
    {
        var health = ProviderHealthService.ClassifyFailure(ProviderId.Claude, message, TimeSpan.Zero);
        Assert.Equal(ProviderHealthState.Quota, health.State);
    }

    [Theory]
    [InlineData("Not logged in. Please run login first.")]
    [InlineData("{\"error\":{\"type\":\"authentication_error\"}}")]
    public void AuthMessages_AreClassifiedAsAuthenticationRequired(string message)
    {
        var health = ProviderHealthService.ClassifyFailure(ProviderId.Claude, message, TimeSpan.Zero);
        Assert.Equal(ProviderHealthState.AuthenticationRequired, health.State);
    }

    [Fact]
    public void UnrecognizedMessage_FallsBackToError()
    {
        var health = ProviderHealthService.ClassifyFailure(ProviderId.Claude, "something exploded", TimeSpan.Zero);
        Assert.Equal(ProviderHealthState.Error, health.State);
    }

    [Theory]
    // 「看起來成功」的輸出裡夾帶限額訊息時，不可以判成上線（GPT 曾因此顯示上線但實際已超額）
    [InlineData("{\"type\":\"turn.completed\"}\n{\"error\":\"rate_limit_error\"}", true)]
    [InlineData("AITEAM_HEALTH_OK usage limit reached", true)]
    [InlineData("{\"type\":\"turn.completed\"}\nAITEAM_HEALTH_OK", false)]
    public void QuotaMessagesAreDetectedEvenInOtherwiseSuccessfulOutput(string output, bool expected)
    {
        Assert.Equal(expected, ProviderHealthService.LooksLikeQuota(output));
    }

    [Theory]
    [InlineData("{\"type\":\"result\",\"is_error\":true,\"result\":\"usage limit\"}", true)]
    [InlineData("{\"type\":\"result\", \"is_error\": true }", true)]
    [InlineData("{\"type\":\"result\",\"is_error\":false,\"result\":\"AITEAM_HEALTH_OK\"}", false)]
    public void JsonErrorFlag_IsDetectedRegardlessOfSpacing(string output, bool expected)
    {
        Assert.Equal(expected, ProviderHealthService.IsJsonErrorResult(output));
    }
}
