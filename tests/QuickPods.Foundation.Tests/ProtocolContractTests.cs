using System.IO;
using QuickPods.Contracts;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void EnvelopeRejectsProtocolMismatch()
    {
        var snapshot = new TaskbarStateSnapshot(TaskbarSurfaceMode.Hidden, 0, false, null);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HostStateEnvelope(QuickPodsProtocol.Version + 1, 0, snapshot));
    }

    [Fact]
    public void VolumeInteractionRequiresBoundedValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HostInteractionEnvelope(
                QuickPodsProtocol.Version,
                1,
                HostInteractionKind.SetVolumeCommit,
                101));

        var interaction = new HostInteractionEnvelope(
            QuickPodsProtocol.Version,
            2,
            HostInteractionKind.SetVolumePreview,
            42);

        Assert.Equal(42, interaction.VolumePercent);
    }

    [Fact]
    public void SequenceGateRejectsDuplicateAndOlderMessages()
    {
        var gate = new MonotonicSequenceGate();

        Assert.True(gate.TryAccept(4));
        Assert.False(gate.TryAccept(4));
        Assert.False(gate.TryAccept(3));
        Assert.True(gate.TryAccept(5));
    }

    [Fact]
    public void JsonLineProtocolRoundTripsBothDirections()
    {
        var snapshot = new TaskbarStateSnapshot(TaskbarSurfaceMode.Native, 64, true, null);
        var state = new HostStateEnvelope(QuickPodsProtocol.Version, 7, snapshot);
        var interaction = new HostInteractionEnvelope(
            QuickPodsProtocol.Version,
            8,
            HostInteractionKind.PreviewAudioFlyout,
            anchor: new TaskbarSurfaceAnchor(100, 900, 400, 940));

        HostStateEnvelope restoredState =
            QuickPodsProtocolJson.DeserializeState(QuickPodsProtocolJson.Serialize(state));
        HostInteractionEnvelope restoredInteraction =
            QuickPodsProtocolJson.DeserializeInteraction(QuickPodsProtocolJson.Serialize(interaction));

        Assert.Equal(state, restoredState);
        Assert.Equal(interaction, restoredInteraction);
    }

    [Fact]
    public void JsonLineProtocolRejectsOversizedInput()
    {
        string oversized = new('x', QuickPodsProtocol.MaximumMessageCharacters + 1);

        Assert.Throws<InvalidDataException>(() => QuickPodsProtocolJson.DeserializeState(oversized));
    }

    [Fact]
    public void ObserverProtocolRoundTripsSanitizedEpochAndInvalidation()
    {
        var request = new ObserverSessionRequest(QuickPodsProtocol.Version, 3, 7);
        var batch = new ObserverInvalidationBatch(
            QuickPodsProtocol.Version,
            5,
            3,
            7,
            ObserverInvalidationKind.StructureChanged |
                ObserverInvalidationKind.BoundingRectangleChanged,
            ObserverSourceClassification.External);

        Assert.Equal(
            request,
            QuickPodsProtocolJson.DeserializeObserverSession(
                QuickPodsProtocolJson.Serialize(request)));
        Assert.Equal(
            batch,
            QuickPodsProtocolJson.DeserializeObserverInvalidation(
                QuickPodsProtocolJson.Serialize(batch)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObserverInvalidationBatch(
            QuickPodsProtocol.Version,
            0,
            0,
            0,
            ObserverInvalidationKind.Ready | ObserverInvalidationKind.StructureChanged,
            ObserverSourceClassification.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObserverInvalidationBatch(
            QuickPodsProtocol.Version,
            0,
            0,
            0,
            ObserverInvalidationKind.StructureChanged,
            (ObserverSourceClassification)99));
    }
}
