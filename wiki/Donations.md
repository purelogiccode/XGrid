# Donations

XGrid is **donationware**. Every feature is available without paying, and a
donation never unlocks extra functionality.

## The banner

A banner under the toolbar stays visible until the donation service confirms a
completed donation for your email. It has no close button: it disappears only
after a donation is recognized.

| Button | Action |
| --- | --- |
| **Donate** | Opens the XGrid donation page. |
| **I already donated** | Asks for the email used on the site and checks it. |

A confirmed donation hides the banner and is stored in `settings.json`. If the
service is unavailable or finds no donation, the banner stays and you can retry
later. The banner is hidden while the shell is fullscreen so it does not cover
gameplay.

## How to donate

1. Open <https://www.purelogiccode.com/donate> with the **Donate** button.
2. Choose **XGrid** as the application.
3. Donate with the email address the app should recognize.
4. Return to XGrid, press **I already donated**, and enter the same email.
5. The banner disappears after the service confirms the donation.

If the site does not list XGrid yet, the product is not registered there.
Contact **contact@purelogiccode.com**.

## Privacy

- The donation email is stored locally in `settings.json`.
- It is sent only to the donation verification API, only when checking.
- It is never logged and never included in bug reports.
- Deleting `settings.json` clears the saved email and the confirmed flag.

The full contract is in the
[donation API reference](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/donation-api.md).
See also [Privacy and data](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/privacy.md).

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| No donation found right after donating | Stripe webhooks can lag; wait a minute and retry. |
| XGrid is not in the application list | The site has not registered the product. |
| Service unavailable | The site or network is down; retry later. |
| Banner returns after saving settings | Fixed in the current build; the Settings page preserves the donation state. |
