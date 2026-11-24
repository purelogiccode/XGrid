# Donation API

The desktop shell checks whether an email has a completed donation for the
`xgrid` application. The service is hosted by PureLogicCode and shared with the
other donationware products.

## Endpoint

| Item | Value |
| --- | --- |
| URL | `https://www.purelogiccode.com/api/donations/verify` |
| Method | `POST` |
| Content type | `application/json` |
| Authentication | `X-API-KEY: <shared desktop key>` |
| Timeout | 10 seconds in the client |

The application slug is `xgrid`. The website catalog registers XGrid as a
donationware product, so the endpoint answers for the slug. A `404` still means
the product is missing from the deployed catalog or the endpoint moved.

## Request

```json
{
  "email": "donor@example.com",
  "applicationSlug": "xgrid"
}
```

The email is trimmed before it is sent. It is never logged.

## Response

```json
{
  "donated": true,
  "applicationSlug": "xgrid",
  "totalAmount": 15.5,
  "lastDonationAtUtc": "2026-10-30T12:00:00Z"
}
```

| Field | Meaning |
| --- | --- |
| `donated` | `true` when at least one completed donation exists for the email and application. |
| `applicationSlug` | Echo of the verified application. |
| `totalAmount` | Total recognized amount, in the service's accounting currency. |
| `lastDonationAtUtc` | UTC timestamp of the most recent completed donation, or `null`. |

A successful response with `donated: false` is a normal "not donated" result,
not an error.

## Status mapping

| HTTP result | Client result | UI behavior |
| --- | --- | --- |
| `200` with `donated: true` | `Donated` | Stores `HasDonated`, hides the banner, shows a thank-you toast. |
| `200` with `donated: false` | `NotDonated` | Keeps the banner and explains that no donation was found yet. |
| `401` or `403` | `Unavailable` (`AuthenticationRejected`) | The API key is rejected; the banner stays and the user is asked to report it. |
| `404` | `Unavailable` (`ApplicationNotRegistered`) | The product is missing from the deployed catalog; the banner stays and says checks are not available yet. |
| `429` | `Unavailable` (`ServiceUnavailable`) | Rate limited; retry later. |
| `5xx` | `Unavailable` (`ServiceUnavailable`) | Temporary service failure; retry later. |
| Timeout or network error | `Unavailable` (`ConnectionFailed`) | The check is best-effort and never interrupts the shell. |
| Empty, malformed or unsupported body | `Unavailable` (`InvalidResponse`) | The service answered but the response could not be used. |

A user who has not donated sees the "no donation was found" message. Every
unavailable reason has its own message, so an outage or a missing catalog entry
is never presented as "you did not donate".

## Client behavior

- A verification is sent only when the user asks for one or when a saved email
  is refreshed once at startup.
- Empty or whitespace-only emails are not sent.
- The response is parsed defensively: empty, malformed or unsupported bodies
  become `Unavailable`.
- The server amount is informational; the banner hides on `donated`, not on a
  minimum amount.
- Concurrent checks are suppressed by the shell.

## Configuration overrides

| Variable | Purpose |
| --- | --- |
| `XGRID_DONATION_ENDPOINT` | Alternate verification URL. |
| `XGRID_DONATION_PAGE` | Alternate public donation page. |
| `XGRID_DONATION_API_KEY` | Alternate shared API key. |

These exist for local integration testing. Production builds use the compiled
defaults.

## Testing

`DonationVerificationClient.VerifyAsync` has an internal overload accepting a
`DonationVerificationOptions` and an `HttpClient`. The diagnostics test project
uses a recording handler to pin the request body, the API-key header, response
parsing and every failure branch without touching the live service.
