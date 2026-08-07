namespace QuickPods.Contracts;

public static class FluentAudioGlyphs
{
    public const string FontFamily = "Segoe Fluent Icons";
    public const string Speaker = "\uE767";
    public const string MutedSpeaker = "\uE74F";
    public const string Headphones = "\uE7F6";

    public static string ForMuteState(bool isMuted) =>
        isMuted ? MutedSpeaker : Speaker;
}
