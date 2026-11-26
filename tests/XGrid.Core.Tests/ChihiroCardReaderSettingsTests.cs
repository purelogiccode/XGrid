using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>Pins the card-reader path rules.</summary>
public sealed class ChihiroCardReaderSettingsTests
{
    [Fact]
    public void Normalize_TrimsPathsAndClearsNulls()
    {
        var cards = new ChihiroCardReaderSettings
        {
            Hw210Slot1 = "  C:\\cards\\p1.bin  ",
            Hw210Slot2 = null!,
            Hw210Gundam = " C:\\cards\\gundam.bin ",
            Crp1231Mt1 = "\tC:\\cards\\mt1.bin\t",
            Crp1231Mt2 = " ",
        };

        cards.Normalize();

        Assert.Equal("C:\\cards\\p1.bin", cards.Hw210Slot1);
        Assert.Equal(string.Empty, cards.Hw210Slot2);
        Assert.Equal("C:\\cards\\gundam.bin", cards.Hw210Gundam);
        Assert.Equal("C:\\cards\\mt1.bin", cards.Crp1231Mt1);
        Assert.Equal(string.Empty, cards.Crp1231Mt2);
    }
}
