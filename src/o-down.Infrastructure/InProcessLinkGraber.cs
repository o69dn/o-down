using o_down.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace o_down.Infrastructure;

public sealed class InProcessLinkGraber : ILinkGraber
{
    private readonly ILogger<InProcessLinkGraber>? _logger;
    public InProcessLinkGraber(ILogger<InProcessLinkGraber>? logger = null) => _logger = logger;
    public event EventHandler<CapturedLink>? LinkCaptured;

    public Task PushAsync(CapturedLink link, CancellationToken ct = default)
    {
        LinkCaptured?.Invoke(this, link);
        return Task.CompletedTask;
    }
}
