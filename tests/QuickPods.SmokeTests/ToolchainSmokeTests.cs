using System.Runtime.InteropServices;
using Xunit;

namespace QuickPods.SmokeTests;

public sealed class ToolchainSmokeTests
{
    [Fact]
    public void TestHostRunsOnWindowsX64()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
    }
}
