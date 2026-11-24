# Development direction

> This page is internal project tracking. The product documentation starts at
> [docs/index.md](index.md).

XGrid is developed as **one product**: the native xemu core plus the Avalonia
shell. The priorities are:

1. **Compatibility.** Keep the core current with xemu 0.8.139 behavior, close
   media gaps and expand the set of titles that boot and play.
2. **Shell quality.** Keep settings, library, input, display, audio, save states
   and diagnostics coherent and testable.
3. **Packaging and licensing.** Ship reproducible archives with the complete
   license set and corresponding source.
4. **Documentation.** Keep the user guide, reference and wiki in sync with the
   shipped behavior.

For the technical plan, see:

- [Architecture](reference/architecture.md)
- [The XGrid core](development/core.md)
- [Building from source](development/building.md)
- [Quality gates](development/quality-gates.md)
- [Releasing](development/releasing.md)
