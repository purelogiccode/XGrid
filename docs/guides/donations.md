# Supporting XGrid

XGrid is **donationware**: every feature is available without paying, and a
donation never unlocks extra functionality. The optional banner on the main
window exists so the project can keep receiving fixes, core updates and release
infrastructure.

## The donation banner

The banner appears under the toolbar until the donation service confirms a
completed donation for the email you used on
[purelogiccode.com](https://www.purelogiccode.com/donate). It has no close
button on purpose: it is removed by a confirmed donation, and it is hidden
temporarily while the shell is fullscreen.

| Action | What happens |
| --- | --- |
| **Donate** | Opens the XGrid donation page. The saved email is included so the site can preselect the application. |
| **I already donated** | Asks for the email used on the website and checks the service. |
| A confirmed donation | The banner disappears immediately and the result is stored in `settings.json`. |
| No donation found | The banner stays and explains that a just-completed donation may need a moment. |
| Service unavailable | The banner stays; nothing is lost and you can retry later. |

The banner is hidden while the shell is fullscreen so it never covers gameplay.
It returns when the window is restored.

## Donating

1. Open the donation page with the banner button, or visit
   <https://www.purelogiccode.com/donate>.
2. Choose **XGrid** as the application.
3. Donate with the email address you want the desktop app to recognize. Signing
   in with the same email is the most reliable option.
4. Return to XGrid and choose **I already donated**. Enter the same email.
5. After the service confirms the donation, the banner is removed.

A donation is linked to the email, not to a machine. Reinstalling XGrid or
copying `settings.json` keeps the confirmed state; entering the same email on
another machine also works.

## What is stored locally

- The donation email is stored in `settings.json` in the XGrid data folder.
- The email is sent only when you press **I already donated** or when the shell
  silently refreshes a previously saved address at startup.
- The email is never written to the log file and is never included in crash or
  bug reports.
- The confirmed `HasDonated` flag is stored next to the email. Deleting
  `settings.json` clears both, and the banner returns; entering the email again
  restores the confirmed state.

## Privacy and network behavior

The check is a single HTTPS `POST` to
`https://www.purelogiccode.com/api/donations/verify` with the email and the
`xgrid` application slug. It does not send firmware paths, game titles, logs or
machine identifiers. See [Privacy and data](../legal/privacy.md) and the
[donation API reference](../reference/donation-api.md).

The endpoint and API key can be overridden for local testing with
`XGRID_DONATION_ENDPOINT`, `XGRID_DONATION_PAGE` and
`XGRID_DONATION_API_KEY`.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| The check says donation checks are not available for XGrid yet | The deployed website catalog no longer lists the `xgrid` product. Contact the site operator. |
| "No donation was found" right after donating | Stripe webhooks can take a moment. Wait a minute and try again. |
| The check says the service is temporarily unavailable or unreachable | The site or network is down. The banner stays and retrying is safe. |
| The banner returned after saving settings | Fixed: the Settings page now preserves the donation state. Update to the current build. |
| The banner returned after deleting settings | Expected. Enter the donation email again. |
| The email was mistyped | Press **I already donated** and enter the correct address. A failed check does not lock the address. |

## Related pages

- [Donation API reference](../reference/donation-api.md)
- [Privacy and data](../legal/privacy.md)
- [Troubleshooting](../reference/troubleshooting.md)
