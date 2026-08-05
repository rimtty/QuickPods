namespace QuickPods.Spike.CoreAudio;

public static class Statistics
{
    public static double Percentile(IEnumerable<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!double.IsFinite(percentile) || percentile is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        double[] ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(values));
        }

        int index = Math.Max(0, (int)Math.Ceiling(percentile * ordered.Length) - 1);
        return ordered[index];
    }

    public static bool HasSustainedGrowth(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        long[] samples = values.ToArray();
        if (samples.Length < 3)
        {
            return false;
        }

        for (int index = 1; index < samples.Length; index++)
        {
            if (samples[index] < samples[index - 1])
            {
                return false;
            }
        }

        // Startup growth that plateaus for the latter half is initialization,
        // not continuously increasing resource use.
        return samples[^1] > samples[samples.Length / 2];
    }
}
