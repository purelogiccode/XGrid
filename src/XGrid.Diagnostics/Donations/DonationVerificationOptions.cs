using System;

namespace XGrid.Diagnostics.Donations;

/// <summary>
/// Configuration for the PureLogicCode donation verification API.
/// </summary>
public sealed record DonationVerificationOptions
{
    /// <summary>The default verification endpoint.</summary>
    public const string DefaultEndpoint = "https://www.purelogiccode.com/api/donations/verify";

    /// <summary>The donation page used by the shell.</summary>
    public const string DefaultDonationPageUrl = "https://www.purelogiccode.com/donate";

    /// <summary>The shared client API key configured by the service.</summary>
    public const string DefaultApiKey =
        "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    /// <summary>The application slug registered for XGrid on the website.</summary>
    public const string DefaultApplicationSlug = "xgrid";

    /// <summary>Gets the verification endpoint.</summary>
    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Gets the public donation page.</summary>
    public string DonationPageUrl { get; init; } = DefaultDonationPageUrl;

    /// <summary>Gets the API key sent in the <c>X-API-KEY</c> header.</summary>
    public string ApiKey { get; init; } = DefaultApiKey;

    /// <summary>Gets the donationware application slug.</summary>
    public string ApplicationSlug { get; init; } = DefaultApplicationSlug;

    /// <summary>
    /// Creates the product defaults, honoring the optional
    /// <c>XGRID_DONATION_ENDPOINT</c>, <c>XGRID_DONATION_API_KEY</c> and
    /// <c>XGRID_DONATION_PAGE</c> overrides.
    /// </summary>
    /// <returns>A configured options instance.</returns>
    public static DonationVerificationOptions CreateDefault()
    {
        return new DonationVerificationOptions
        {
            Endpoint = ReadSetting("XGRID_DONATION_ENDPOINT", DefaultEndpoint),
            DonationPageUrl = ReadSetting("XGRID_DONATION_PAGE", DefaultDonationPageUrl),
            ApiKey = ReadSetting("XGRID_DONATION_API_KEY", DefaultApiKey),
        };
    }

    private static string ReadSetting(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
