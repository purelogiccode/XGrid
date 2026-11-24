using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;
using DonationReason = XGrid.Diagnostics.Donations.DonationVerificationUnavailableReason;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the donation failure copy so a missing product or an outage is never
/// shown as "you did not donate".
/// </summary>
public sealed class DonationStatusTests
{
    [Fact]
    public void NotDonated_IsNotAReachableFailureCopy()
    {
        string copy = MainWindowViewModel.DescribeDonationFailure(
            DonationReason.None);

        Assert.DoesNotContain("did not", copy, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no donation", copy, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DonationReason.ApplicationNotRegistered, "not available for XGrid yet")]
    [InlineData(DonationReason.AuthenticationRejected, "rejected")]
    [InlineData(DonationReason.ConnectionFailed, "internet connection")]
    [InlineData(DonationReason.InvalidResponse, "unexpected response")]
    [InlineData(DonationReason.ServiceUnavailable, "temporarily unavailable")]
    [InlineData(DonationReason.None, "temporarily unavailable")]
    public void DescribeDonationFailure_ExplainsTheCause(
        DonationReason reason,
        string expected)
    {
        string copy = MainWindowViewModel.DescribeDonationFailure(reason);

        Assert.Contains(expected, copy, System.StringComparison.OrdinalIgnoreCase);
    }
}
