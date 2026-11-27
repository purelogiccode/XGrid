# Supporting XGrid

XGrid is **donationware**: every feature works without paying. The optional
banner is the only difference and disappears after a confirmed donation.

## How to donate

1. Open <https://www.purelogiccode.com/donate> or click **Donate**.
2. Choose **XGrid** and donate with the email you want the app to recognize.
3. In XGrid, click **I already donated** and enter the same email.
4. The banner is removed when the service confirms the donation.

The donation is linked to the email, not the machine. Reinstalling or copying
`settings.json` keeps the confirmed state.

## What is stored

- The email and `HasDonated` flag are stored in `settings.json`.
- The email is sent only to the donation verification endpoint.
- It is never logged or included in bug reports.
- Deleting `settings.json` clears the state; entering the email again restores it.

## Privacy

The check sends the email and the `xgrid` application slug. It does not send
firmware paths, game titles, logs or machine identifiers.

## Troubleshooting

- "No donation found" right after donating: payment webhooks can take a minute.
- Service unavailable: retry later; the banner stays.
- Mistyped email: press **I already donated** and enter the correct address.
- Checks not available for XGrid: the website catalog may not list `xgrid`.

Full guide: [Supporting XGrid](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/donations.md)
and [Donation API](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/donation-api.md).
