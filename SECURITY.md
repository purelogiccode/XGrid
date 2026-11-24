# Security policy

XGrid processes untrusted game images, executables, firmware and media supplied
by the user. Treat all input as hostile.

## Reporting a vulnerability

Open a **private security advisory** on the repository (Security → Advisories) or
contact the maintainers privately. Do not file a public issue for a
vulnerability.

Include:

- a description of the issue;
- reproduction steps or a minimal file;
- the affected component (shell, core, media driver, packaging);
- the XGrid version and platform;
- any crash address or log excerpt that helps.

## Scope

- Host memory-safety issues reachable from guest input (XBE, XDVDFS, FATX, ISO,
  CHD, ZAR, NV2A command streams).
- Unbounded allocation, integer overflow or infinite loops triggered by malformed
  images.
- Guest-to-host escape (file-system access, code execution, path traversal).
- Vulnerabilities in the packaged runtime libraries that XGrid redistributes.
- Supply-chain issues in the build or release scripts.

## Non-goals

- Guest crashes, hangs or incorrect emulation that only affect the emulated Xbox
  are bugs, not security issues. Report them through the normal issue tracker.
- Firmware or game images you do not have the right to use.
- Unsupported Linux/macOS core configurations.

## Supported versions

Security fixes target the latest release and the current development branch. There
is no long-term support for older alpha releases.

## Disclosure

Please allow time for a fix and a coordinated release before publishing details.
Credit will be given unless you ask to remain anonymous.
