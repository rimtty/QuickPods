using QuickPods.Contracts;
using QuickPods.TaskbarHost.Runtime;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class ObserverLifecycleNotificationPolicyTests
{
    [Theory]
    [InlineData(ObserverInvalidationKind.ExplorerGenerationChanged, false, HostInteractionKind.TaskbarObserverGenerationChanged)]
    [InlineData(ObserverInvalidationKind.ObserverFaulted, false, HostInteractionKind.TaskbarObserverFaulted)]
    [InlineData(ObserverInvalidationKind.None, true, HostInteractionKind.TaskbarObserverFaulted)]
    [InlineData(ObserverInvalidationKind.None, false, HostInteractionKind.TaskbarObserverDisconnected)]
    public void RetirementReasonIsClassifiedWithoutProcessOrWindowIdentity(
        ObserverInvalidationKind terminalKinds,
        bool hasTransportFailure,
        HostInteractionKind expected)
    {
        Assert.Equal(
            expected,
            ObserverLifecycleNotificationPolicy.ClassifyRetirement(
                terminalKinds,
                hasTransportFailure));
    }
}
