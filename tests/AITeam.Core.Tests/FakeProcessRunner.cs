using AITeam.Models;
using AITeam.Services;

namespace AITeam.Core.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<IReadOnlyList<string>, ProcessRunResult> _handler;

    public FakeProcessRunner(Func<IReadOnlyList<string>, ProcessRunResult> handler) => _handler = handler;

    /// <summary>假的執行器不會真的產生輸出，所以 onOutputLine 只是把每一行送一次，方便測試串流回呼。</summary>
    public Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null)
    {
        var result = _handler(arguments.ToList());

        if (onOutputLine is not null)
        {
            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                onOutputLine(line.TrimEnd('\r'));
        }

        return Task.FromResult(result);
    }
}
