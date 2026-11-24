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

/// <summary>
/// Why a verification request could not be completed. The shell uses this to tell
/// a user who has not donated apart from a service that is temporarily unable to
/// answer.
/// </summary>
public enum DonationVerificationUnavailableReason
{
    /// <summary>The request completed, so no failure reason applies.</summary>
    None,

    /// <summary>The service does not recognize the application slug yet.</summary>
    ApplicationNotRegistered,

    /// <summary>The service rejected the configured API key.</summary>
    AuthenticationRejected,

    /// <summary>The service is overloaded or temporarily unavailable.</summary>
    ServiceUnavailable,

    /// <summary>The service could not be reached or the request timed out.</summary>
    ConnectionFailed,

    /// <summary>The service answered with an empty, malformed or unsupported body.</summary>
    InvalidResponse,
}

/// <summary>Details returned by a successful donation verification request.</summary>
/// <param name="Status">The verification outcome.</param>
/// <param name="TotalAmount">The total recognized donation amount, when donated.</param>
/// <param name="LastDonationAtUtc">The UTC timestamp of the most recent donation, when known.</param>
/// <param name="Reason">Why verification was unavailable, when it was.</param>
public sealed record DonationVerificationResult(
    DonationVerificationStatus Status,
    decimal TotalAmount = 0m,
    DateTime? LastDonationAtUtc = null,
    DonationVerificationUnavailableReason Reason = DonationVerificationUnavailableReason.None);
