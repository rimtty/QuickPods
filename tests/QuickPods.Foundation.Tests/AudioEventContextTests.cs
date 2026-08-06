using QuickPods.Windows.Audio;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class AudioEventContextTests
{
    [Fact]
    public void ContextIsUniqueToEachAudioPortInstance()
    {
        AudioEventContext first = AudioEventContext.Create();
        AudioEventContext second = AudioEventContext.Create();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first.Value, second.Value);
        Assert.True(first.IsSelfOriginated(first.Value));
        Assert.False(first.IsSelfOriginated(second.Value));
    }
}
