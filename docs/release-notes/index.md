# Release notes

XGrid releases use semantic versions. The product version is defined once in
`Directory.Build.props`; every archive carries a `VERSION` file and the matching
notes.

| Version | Date | Summary |
| --- | --- | --- |
| [0.1.0](v0.1.0.md) | First alpha | Avalonia shell, xemu 0.8.139 core, real-time media, save states, Sega Chihiro |

## Reading the notes

- **Highlights** are the user-visible changes worth knowing before you update.
- **Known limitations** are still open in that build; the
  [Compatibility](../reference/compatibility.md) page tracks the current
  supported surface.
- **Downloads** lists the archives produced by the release tree. Windows and
  Linux x64 frontend archives include the core; macOS archives contain the shell
  only.

## Release process

See [Releasing](../development/releasing.md) for the build, verification,
signing and publication process.
