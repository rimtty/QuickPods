namespace QuickPods.Windows;

public enum WindowsAdapterImplementation
{
    Pending,
    Available,
    Unavailable,
}

public sealed record WindowsPlatformBoundary(
    WindowsAdapterImplementation CoreAudio,
    WindowsAdapterImplementation BluetoothKernelStreaming,
    WindowsAdapterImplementation DefaultEndpointPolicy)
{
    public static WindowsPlatformBoundary PhaseOne { get; } = new(
        WindowsAdapterImplementation.Pending,
        WindowsAdapterImplementation.Pending,
        WindowsAdapterImplementation.Pending);
}
