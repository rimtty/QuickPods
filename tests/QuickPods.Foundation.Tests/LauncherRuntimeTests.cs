using System.Diagnostics;
using System.IO;
using QuickPods.Launcher;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class LauncherRuntimeTests : IDisposable
{
    private readonly string launcherDirectory = Path.Combine(
        Path.GetTempPath(),
        "QuickPods.Launcher.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingApplicationReportsAnActionableError()
    {
        string? error = null;
        bool started = false;

        int exitCode = LauncherRuntime.Run(
            [],
            launcherDirectory,
            (_, _) =>
            {
                started = true;
                return 0;
            },
            message => error = message);

        Assert.Equal(2, exitCode);
        Assert.False(started);
        Assert.Contains("Re-extract", error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("app", "QuickPods.exe"), error, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchesTheInternalApplicationWithArgumentsAndWorkingDirectory()
    {
        string applicationDirectory = CreateApplicationPlaceholder();
        ProcessStartInfo? captured = null;
        bool? capturedWait = null;

        int exitCode = LauncherRuntime.Run(
            ["--background", "value with spaces"],
            launcherDirectory,
            (startInfo, waitForExit) =>
            {
                captured = startInfo;
                capturedWait = waitForExit;
                return 0;
            },
            _ => Assert.Fail("The launcher unexpectedly reported an error."));

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal(Path.Combine(applicationDirectory, "QuickPods.exe"), captured.FileName);
        Assert.Equal(applicationDirectory, captured.WorkingDirectory);
        Assert.False(captured.UseShellExecute);
        Assert.Equal(["--background", "value with spaces"], captured.ArgumentList);
        Assert.False(capturedWait);
    }

    [Fact]
    public void WaitsForStartupCleanupAndReturnsTheApplicationExitCode()
    {
        _ = CreateApplicationPlaceholder();
        bool? capturedWait = null;

        int exitCode = LauncherRuntime.Run(
            ["--unregister-startup"],
            launcherDirectory,
            (_, waitForExit) =>
            {
                capturedWait = waitForExit;
                return 17;
            },
            _ => Assert.Fail("The launcher unexpectedly reported an error."));

        Assert.Equal(17, exitCode);
        Assert.True(capturedWait);
    }

    [Fact]
    public void LaunchFailureReturnsAnErrorWithoutLeakingImplementationDetails()
    {
        _ = CreateApplicationPlaceholder();
        string? error = null;

        int exitCode = LauncherRuntime.Run(
            [],
            launcherDirectory,
            (_, _) => throw new InvalidOperationException("test failure"),
            message => error = message);

        Assert.Equal(3, exitCode);
        Assert.Contains("could not start", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test failure", error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(launcherDirectory))
        {
            Directory.Delete(launcherDirectory, recursive: true);
        }
    }

    private string CreateApplicationPlaceholder()
    {
        string applicationDirectory = Path.Combine(launcherDirectory, "app");
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllBytes(Path.Combine(applicationDirectory, "QuickPods.exe"), []);
        return applicationDirectory;
    }
}
