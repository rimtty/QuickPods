namespace QuickPods.Core;

public static class VolumeMath
{
    public static int ClampPercent(int volumePercent) => Math.Clamp(volumePercent, 0, 100);

    public static float PercentToScalar(int volumePercent) => ClampPercent(volumePercent) / 100f;

    public static int ScalarToPercent(float scalar)
    {
        if (!float.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(nameof(scalar), scalar, "Volume must be finite.");
        }

        float bounded = Math.Clamp(scalar, 0f, 1f);
        return (int)Math.Round(bounded * 100f, MidpointRounding.AwayFromZero);
    }
}
