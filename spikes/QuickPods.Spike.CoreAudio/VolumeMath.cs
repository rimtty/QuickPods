namespace QuickPods.Spike.CoreAudio;

public static class VolumeMath
{
    public static double ClampPercent(double percent)
    {
        if (!double.IsFinite(percent))
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "The volume percentage must be finite.");
        }

        return Math.Clamp(percent, 0d, 100d);
    }

    public static float PercentToScalar(double percent)
    {
        return (float)(ClampPercent(percent) / 100d);
    }

    public static double ScalarToPercent(float scalar)
    {
        if (!float.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(nameof(scalar), scalar, "The volume scalar must be finite.");
        }

        return Math.Clamp(scalar, 0f, 1f) * 100d;
    }

    public static int ScalarToDisplayPercent(float scalar)
    {
        return (int)Math.Round(ScalarToPercent(scalar), MidpointRounding.AwayFromZero);
    }
}
