namespace QuickPods.Windows.Audio;

internal sealed class AudioEventContext
{
    private AudioEventContext(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static AudioEventContext Create() => new(Guid.NewGuid());

    public bool IsSelfOriginated(Guid candidate) => candidate == Value;
}
