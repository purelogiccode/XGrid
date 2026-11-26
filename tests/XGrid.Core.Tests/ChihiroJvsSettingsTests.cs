using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>Pins the Gundam cockpit binding rules.</summary>
public sealed class ChihiroJvsSettingsTests
{
    [Fact]
    public void Normalize_RepairsGundamBindings()
    {
        var settings = new ChihiroJvsSettings
        {
            GundamLUp = 3,
            GundamRTrigger = 1009,
            GundamPedal = 44,
            GsChange = 1008,
        };

        settings.Normalize();

        Assert.Equal(0, settings.GundamLUp);
        Assert.Equal(0, settings.GundamRTrigger);
        Assert.Equal(44, settings.GundamPedal);
        Assert.Equal(1008, settings.GsChange);
    }
}
