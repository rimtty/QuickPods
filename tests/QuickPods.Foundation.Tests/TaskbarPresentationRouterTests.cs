using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;
using QuickPods.TaskbarHost.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarPresentationRouterTests
{
    [Fact]
    public void CompleteEvidenceRoutesPlaceNativeAndNoFitFloating()
    {
        TaskbarDiscoveryResult discovery = CompleteDiscovery();
        var nativeBounds = new PixelRect(700, 1040, 1000, 1080);

        TaskbarPresentationRoute native = TaskbarPresentationRouter.Select(
            discovery,
            TaskbarPlacementResult.Place(nativeBounds, TaskbarStripMode.Standard));
        TaskbarPresentationRoute floating = TaskbarPresentationRouter.Select(
            discovery,
            TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth));

        Assert.Equal(TaskbarPresentationSurface.Native, native.Surface);
        Assert.Equal(nativeBounds, native.Bounds);
        Assert.Equal(TaskbarPresentationSurface.Floating, floating.Surface);
        Assert.Equal(new PixelRect(810, 992, 1110, 1032), floating.Bounds);
    }

    [Fact]
    public void UnknownOrInconsistentEvidenceAlwaysRoutesHidden()
    {
        var incomplete = new TaskbarDiscoveryResult(null, [TaskbarDiscoveryFault.PrimaryTaskbarMissing], false);
        TaskbarPresentationRoute unknown = TaskbarPresentationRouter.Select(
            incomplete,
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation));
        TaskbarPresentationRoute inconsistentNoFit = TaskbarPresentationRouter.Select(
            incomplete,
            TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth));

        Assert.Equal(TaskbarPresentationSurface.Hidden, unknown.Surface);
        Assert.Equal(TaskbarPresentationReason.TransientUnknownHidden, unknown.Reason);
        Assert.Equal(TaskbarPresentationSurface.Hidden, inconsistentNoFit.Surface);
        Assert.Equal(TaskbarPresentationReason.InconsistentEvidenceHidden, inconsistentNoFit.Reason);
        Assert.Null(unknown.Bounds);
        Assert.Null(inconsistentNoFit.Bounds);
    }

    [Fact]
    public void KnownUnsupportedAlignmentAlwaysRoutesHidden()
    {
        TaskbarPresentationRoute route = TaskbarPresentationRouter.Select(
            CompleteDiscovery(),
            TaskbarPlacementResult.UnsupportedConfiguration(
                PlacementReason.UnsupportedAlignment));

        Assert.Equal(TaskbarPresentationSurface.Hidden, route.Surface);
        Assert.Equal(TaskbarPresentationReason.UnsupportedConfigurationHidden, route.Reason);
        Assert.Null(route.Bounds);
    }

    [Fact]
    public void StableNativeSurfaceIsRetainedOnlyForSamePlacementOrTransientUnknown()
    {
        TaskbarDiscoveryResult complete = CompleteDiscovery();
        TaskbarPresentationRoute sameNative = TaskbarPresentationRouter.Select(
            complete,
            TaskbarPlacementResult.Place(
                new PixelRect(700, 1040, 1000, 1080),
                TaskbarStripMode.Standard));
        TaskbarPresentationRoute transient = TaskbarPresentationRouter.Select(
            new TaskbarDiscoveryResult(
                null,
                [TaskbarDiscoveryFault.PrimaryTaskbarMissing],
                false),
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation));
        TaskbarPresentationRoute unsupported = TaskbarPresentationRouter.Select(
            complete,
            TaskbarPlacementResult.UnsupportedConfiguration(
                PlacementReason.UnsupportedAlignment));

        Assert.True(TaskbarContinuityPolicy.CanRetainNativeSurface(
            sameNative,
            placementIdentityMatches: true,
            continuityStable: true));
        Assert.True(TaskbarContinuityPolicy.CanRetainNativeSurface(
            transient,
            placementIdentityMatches: false,
            continuityStable: true));
        Assert.False(TaskbarContinuityPolicy.CanRetainNativeSurface(
            unsupported,
            placementIdentityMatches: false,
            continuityStable: true));
        Assert.False(TaskbarContinuityPolicy.CanRetainNativeSurface(
            transient,
            placementIdentityMatches: false,
            continuityStable: false));
        Assert.True(TaskbarContinuityPolicy.ShouldEnsureObserverAfterRetention(sameNative));
        Assert.False(TaskbarContinuityPolicy.ShouldEnsureObserverAfterRetention(transient));
    }

    private static TaskbarDiscoveryResult CompleteDiscovery()
    {
        var snapshot = new LiveTaskbarSnapshot(
            new nint(1),
            10,
            new PixelRect(0, 1040, 1920, 1080),
            96,
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            [],
            [new AutomationButtonSnapshot("StartButton", new PixelRect(900, 1040, 940, 1080))]);
        return new(snapshot, [], false);
    }
}
