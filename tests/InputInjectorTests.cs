using BayControl.Host.Services;

namespace BayControl.Tests;

public class InputInjectorTests
{
    [Theory]
    [InlineData("a", 0x41)]
    [InlineData("m", 0x4D)]
    [InlineData("esc", 0x1B)]
    [InlineData("f3", 0x72)]
    [InlineData("left", 0x25)]
    [InlineData("[", 0xDB)]
    [InlineData("oem_4", 0xDB)]
    public void MapKey_known_keys(string key, int expected)
    {
        Assert.Equal((ushort)expected, InputInjector.MapKey(key));
    }

    [Fact]
    public void MapKey_rejects_unknown()
    {
        Assert.Throws<ArgumentException>(() => InputInjector.MapKey("not-a-key"));
    }
}
