namespace QuickPods.Spike.DefaultEndpointPolicy;

internal enum AudioDataFlow
{
    Render,
    Capture,
}

internal enum BluetoothAudioProfile
{
    Stereo,
    HandsFree,
    Unknown,
}

internal enum AudioEndpointAvailability
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
}

internal enum DefaultEndpointRole
{
    Console,
    Multimedia,
    Communications,
}

internal sealed class OpaqueEndpointHandle(string value)
    : IEquatable<OpaqueEndpointHandle>
{
    internal string Value { get; } = !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException("An endpoint handle cannot be empty.", nameof(value));

    public bool Equals(OpaqueEndpointHandle? other) =>
        other is not null && StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => Equals(obj as OpaqueEndpointHandle);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => "<endpoint>";
}

internal sealed class OpaqueContainerHandle(string value)
    : IEquatable<OpaqueContainerHandle>
{
    internal string Value { get; } = !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException("A Container handle cannot be empty.", nameof(value));

    public bool Equals(OpaqueContainerHandle? other) =>
        other is not null && StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => Equals(obj as OpaqueContainerHandle);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => "<container>";
}

internal sealed record DefaultEndpointCandidate(
    OpaqueEndpointHandle Endpoint,
    OpaqueContainerHandle Container,
    AudioDataFlow Flow,
    BluetoothAudioProfile Profile,
    AudioEndpointAvailability Availability);

internal enum DefaultEndpointTargetStatus
{
    Selected,
    NoActiveStereoEndpoint,
    AmbiguousActiveStereoEndpoints,
}

internal sealed record DefaultEndpointTargetResolution(
    DefaultEndpointTargetStatus Status,
    DefaultEndpointCandidate? Target);

internal readonly record struct DefaultEndpointWriteResult(
    bool Accepted,
    int? HResult);

internal readonly record struct DefaultEndpointNotification(
    DefaultEndpointRole Role,
    long Generation,
    bool Observed);

internal sealed record DefaultEndpointRoleEvidence(
    DefaultEndpointRole Role,
    bool ChangeRequired,
    bool WriteAccepted,
    bool NotificationObserved,
    bool ReadBackMatched,
    int? HResult);

internal enum DefaultEndpointSwitchOutcome
{
    Applied,
    AlreadyDefault,
    Superseded,
    WriteRejected,
    Partial,
    VerificationFailed,
    CommunicationsChanged,
}

internal sealed record DefaultEndpointSwitchResult(
    DefaultEndpointSwitchOutcome Outcome,
    long Generation,
    IReadOnlyList<DefaultEndpointRoleEvidence> Roles,
    bool CommunicationsUnchanged)
{
    internal bool Succeeded =>
        Outcome is DefaultEndpointSwitchOutcome.Applied or
            DefaultEndpointSwitchOutcome.AlreadyDefault;

    internal bool HasAcceptedMutation => Roles.Any(role => role.WriteAccepted);
}
