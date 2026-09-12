using AITeam.Models;
using AITeam.Services;

namespace AITeam.Core.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<IReadOnlyList<string>, ProcessRunResult> _handler;

    public FakeProcessRunner(Func<IReadOnlyList<string>, ProcessRunResult> handler) => _handler = handler;

    public Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(_handler(arguments.ToList()));
}
