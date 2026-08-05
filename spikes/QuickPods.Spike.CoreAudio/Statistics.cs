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

    public static bool HasNonDecreasingGrowth(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        using IEnumerator<long> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        long first = enumerator.Current;
        long previous = first;
        int comparisons = 0;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current < previous)
            {
                return false;
            }

            previous = enumerator.Current;
            comparisons++;
        }

        return comparisons > 0 && previous > first;
    }
}
