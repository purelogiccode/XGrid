using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>Pins the player-2 cabinet binding rules.</summary>
public sealed class ChihiroJvsP2SettingsTests
{
    [Fact]
    public void Normalize_RepairsBindingsAndAimSpeed()
    {
        var settings = new ChihiroJvsP2Settings
        {
            Start = 9999,
            Coin = -1,
            CardIn = 1001,
            AimLeft = 3,
            AimRight = 79,
            AimSpeed = 900,
            GsTrigger = 1008,
        };

        settings.Normalize();

        Assert.Equal(0, settings.Start);
        Assert.Equal(0, settings.Coin);
        Assert.Equal(1001, settings.CardIn);
        Assert.Equal(0, settings.AimLeft);
        Assert.Equal(79, settings.AimRight);
        Assert.Equal(400, settings.AimSpeed);
        Assert.Equal(1008, settings.GsTrigger);
    }
}
