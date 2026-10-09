# Security Policy

XGrid processes untrusted game images, executables, and firmware supplied by the user. Treat all
input as hostile.

## Reporting a vulnerability

Open a private security advisory or contact the maintainers rather than filing a public issue.
Include a description, reproduction steps, and the affected component.

## Scope

- Host memory-safety issues reachable from guest input (XBE, XDVDFS, FATX, ISO, NV2A command
  streams).
- Unbounded allocation, integer overflow, or infinite loops triggered by malformed images.
- Any guest-to-host escape (file system access, code execution, path traversal).

## Non-goals

Guest crashes, hangs, or incorrect emulation that only affect the emulated Xbox are bugs, not
security issues. Report those through the normal issue tracker.
