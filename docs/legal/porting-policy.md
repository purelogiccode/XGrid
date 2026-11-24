# Porting and provenance

XGrid may port code from GPL-compatible projects when that is faster and safer
than re-implementing it. Provenance is part of the code, not an afterthought.

## Allowed sources

XGrid's own code is GPL-2.0-or-later, so code may be ported from:

- xemu and its QEMU base;
- QEMU;
- Cxbx-Reloaded;
- xqemu;
- envytools;
- nv2a-trace;
- other GPL-2.0-compatible sources.

**GPL-3.0 code** (for example xbased) may be ported only when the build-scoped
audit proves the combined build is GPL-3.0-compatible. Until then it is facts and
architecture reference only.

## Rules

1. **Keep the notice.** A ported file keeps its original copyright and license
   header.
2. **Mark it.** Add an SPDX identifier and a line of the form:
   ```text
   Ported from <project> <version> <path>
   ```
3. **Record it.** Add the file and source to `THIRD_PARTY_NOTICES.md` and the
   licensing page.
4. **Test it.** A port needs a differential test that fails before the port and
   passes after it, or an equally strong acceptance test.
5. **Keep the license compatible.** Do not add code whose license cannot be
   combined with GPL-2.0-or-later.
6. **Do not copy firmware or game data.** Reference material is facts and source,
   not copyrighted content.

## P/Invoke and native edges

XGrid is C# first for its own code. P/Invoke is allowed for host APIs and OS
facilities, and each edge is inventoried in the project's porting records. The
native emulator core is a deliberate exception documented in
[Architecture](../reference/architecture.md).

## Review checklist

- [ ] Original copyright/license notice preserved.
- [ ] SPDX identifier present.
- [ ] `Ported from` line names project, version and path.
- [ ] `THIRD_PARTY_NOTICES.md` updated.
- [ ] License text shipped when required.
- [ ] Differential/acceptance test added.
- [ ] Documentation updated.

## GPL-3.0 audit

Before any GPL-3.0 source is compiled into a distributed binary, the audit must
show that every compiled file is “or later” or otherwise compatible. Record the
result with the release. Without that audit, GPL-3.0 code stays reference-only.

## Reference material

The `References2/` tree, when present, is a development-only oracle. It is not
part of the build and is never redistributed. Do not modify it.
