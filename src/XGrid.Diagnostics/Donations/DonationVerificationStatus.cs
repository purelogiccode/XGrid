using System;

namespace XGrid.Diagnostics.Donations;

/// <summary>The outcome of a donation verification request.</summary>
public enum DonationVerificationStatus
{
    /// <summary>A completed donation was confirmed for the application and email.</summary>
    Donated,

    /// <summary>The server responded and found no completed donation.</summary>
    NotDonated,

    /// <summary>The server could not be reached or returned an unexpected response.</summary>
    Unavailable,
}

/// <summary>Details returned by a successful donation verification request.</summary>
/// <param name="Status">The verification outcome.</param>
/// <param name="TotalAmount">The total recognized donation amount, when donated.</param>
/// <param name="LastDonationAtUtc">The UTC timestamp of the most recent donation, when known.</param>
public sealed record DonationVerificationResult(
    DonationVerificationStatus Status,
    decimal TotalAmount = 0m,
    DateTime? LastDonationAtUtc = null);
