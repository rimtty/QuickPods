using QuickPods.Infrastructure.Runtime;
using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class ProductLifetimeTests
{
    [Fact]
    public async Task SecondaryInstanceSignalsPrimaryAndClosePolicyRequiresExplicitExit()
    {
        string applicationId = $"QuickPods.Foundation.Tests.{Guid.NewGuid():N}";
        using SingleInstanceLease primary = SingleInstanceLease.TryAcquire(applicationId);
        using SingleInstanceLease secondary = SingleInstanceLease.TryAcquire(applicationId);
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.ActivationRequested += (_, _) => activation.TrySetResult();
        primary.StartActivationListener();

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        secondary.SignalPrimary();
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var policy = new ProductLifetimePolicy();
        Assert.True(policy.ShouldHideMainWindowOnClose);
        policy.RequestExit();
        Assert.True(policy.IsExitRequested);
        Assert.False(policy.ShouldHideMainWindowOnClose);
    }
}
