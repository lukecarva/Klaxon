using Microsoft.Extensions.Hosting;

namespace Klaxon.App.Services;

/// <summary>No-op host lifetime for a WPF tray app; the <see cref="App"/> class owns the process
/// lifecycle.</summary>
public sealed class WpfHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
