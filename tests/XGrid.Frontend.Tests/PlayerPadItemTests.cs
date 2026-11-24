using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the Settings → Input player rows: a persisted XInput slot maps to one
/// choice, "No gamepad" is -1, and an unknown slot falls back to no gamepad.
/// </summary>
public sealed class PlayerPadItemTests
{
    [Fact]
    public void EachSlotRoundTripsThroughTheRow()
    {
        for (int slot = -1; slot < 4; slot++)
        {
            var item = new PlayerPadItem(player: 2, slot);
            Assert.Equal(slot, item.ToSlot());
        }
    }

    [Fact]
    public void TheNoneChoiceMapsToMinusOne()
    {
        var item = new PlayerPadItem(player: 1, slot: -1);

        Assert.Equal("No gamepad", item.Selected.Label);
        Assert.Equal(-1, item.ToSlot());
    }

    [Fact]
    public void AnUnknownSlotFallsBackToNoGamepad()
    {
        var item = new PlayerPadItem(player: 3, slot: 9);

        Assert.Equal("No gamepad", item.Selected.Label);
        Assert.Equal(-1, item.ToSlot());
    }
}
