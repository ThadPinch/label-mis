using FluentAssertions;
using LabelsMis.Infrastructure.Fedex;
using LabelsMis.Web.Background;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The tracking poller must stay idle against the sandbox carrier: the sandbox fabricates
/// fresh-timestamped events on every call, so polling it inserts rows forever and grows the
/// process until the container is recycled (seen in production, Sep 2026).
/// </summary>
public class ShipmentTrackingPollerTests
{
    [Fact]
    public void ShouldPoll_WhenSandbox_ReturnsFalse() =>
        ShipmentTrackingPoller.ShouldPoll(new FedexOptions { UseSandbox = true }).Should().BeFalse();

    [Fact]
    public void ShouldPoll_WhenRealCarrier_ReturnsTrue() =>
        ShipmentTrackingPoller.ShouldPoll(new FedexOptions { UseSandbox = false }).Should().BeTrue();

    [Fact]
    public async Task ExecuteAsync_WhenSandbox_ExitsWithoutTouchingServices()
    {
        // A provider that throws on any resolution proves the poller never opened a scope.
        var poller = new ShipmentTrackingPoller(
            new ThrowingServiceProvider(),
            Options.Create(new FedexOptions { UseSandbox = true }),
            NullLogger<ShipmentTrackingPoller>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await poller.StartAsync(cts.Token);
        var run = poller.ExecuteTask;

        run.Should().NotBeNull();
        await run!.WaitAsync(cts.Token);
        run.IsCompletedSuccessfully.Should().BeTrue();
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            throw new InvalidOperationException($"Poller resolved {serviceType.Name} while disabled.");
    }
}
