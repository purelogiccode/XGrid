using Avalonia.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

public sealed class ChihiroKeysTests
{
    [Theory]
    [InlineData(PhysicalKey.Digit5, 34)]
    [InlineData(PhysicalKey.Enter, 40)]
    [InlineData(PhysicalKey.L, 15)]
    [InlineData(PhysicalKey.K, 14)]
    [InlineData(PhysicalKey.C, 6)]
    [InlineData(PhysicalKey.BracketLeft, 47)]
    [InlineData(PhysicalKey.BracketRight, 48)]
    [InlineData(PhysicalKey.ArrowLeft, 80)]
    [InlineData(PhysicalKey.ArrowRight, 79)]
    public void ToScancode_MapsTheCabinetKeys(PhysicalKey key, int expected)
    {
        Assert.Equal(expected, ChihiroKeys.ToScancode(key));
    }

    [Theory]
    [InlineData(0, "Unbound")]
    [InlineData(34, "5")]
    [InlineData(40, "Enter")]
    [InlineData(48, "]")]
    [InlineData(1001, "Mouse left")]
    [InlineData(1003, "Mouse right")]
    public void Describe_NamesBindings(int binding, string expected)
    {
        Assert.Equal(expected, ChihiroKeys.Describe(binding));
    }

    [Fact]
    public void BindingItem_CapturesAndClears()
    {
        int value = 0;
        var item = new ChihiroBindingItem(
            "Coin",
            () => value,
            updated => value = updated,
            _ => { });

        item.Capture(PhysicalKey.Digit5);
        Assert.Equal(34, value);
        Assert.Equal("5", item.KeysDisplay);
        Assert.False(item.IsCapturing);

        item.ClearCommand.Execute(null);
        Assert.Equal(0, value);
        Assert.Equal("Unbound", item.KeysDisplay);
    }
}
