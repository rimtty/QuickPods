namespace QuickPods.Spike.TaskbarHost.Geometry;

/// <summary>
/// A half-open rectangle expressed in device-independent pixels (DIP).
/// </summary>
internal readonly record struct DipRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool IsValid =>
        double.IsFinite(Left) &&
        double.IsFinite(Top) &&
        double.IsFinite(Right) &&
        double.IsFinite(Bottom) &&
        Left < Right &&
        Top < Bottom &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);
}
