# Glossary

| Term | Meaning |
| --- | --- |
| **APU** | Audio Processing Unit in the MCPX chip; hosts the voice processor and DSP. |
| **AV pack** | The video output type the console reports to the guest (HDTV, composite, SCART, VGA, …). |
| **CCI** | Cerbios/Repackinator compressed Xbox disc image; single or split parts. |
| **CHD** | Compressed Hunks of Data, a compressed disc-image format. |
| **Core** | The native XGrid emulator core in `xemu/`, originally derived from xemu 0.8.139 and now evolving separately. |
| **DIMM** | Chihiro game-memory board; the netboot image is loaded into it. |
| **DSP** | The guest audio microcode executed by the APU. |
| **EEPROM** | 256-byte console configuration storage. |
| **Frame sink** | Shared-memory path used by headless runs to count rendered frames. The shell never presents it. |
| **HRTF** | Head-related transfer function; spatial audio filtering. |
| **In-process** | Hosting mode where the core library runs inside the shell process and exposes save states, playback and disc swap. |
| **Input sink** | Shared-memory path where the shell publishes pad records for the core. |
| **ISO/XISO** | An XDVDFS disc image; optimized or full Redump. |
| **JVS** | JAMMA Video Standard; the Chihiro cabinet's control and I/O bus. |
| **MCPX** | The Xbox southbridge/multimedia chip; also the boot ROM. |
| **NV2A** | The Xbox GPU. |
| **QEMU TCG** | Tiny Code Generator, the dynamic translation CPU core used by xemu. |
| **Save state** | A QEMU/xemu snapshot of the machine stored in the HDD image or Chihiro snapshot store. |
| **Separate process** | Hosting mode where the core runs as `xemu.exe` beside the shell. |
| **Shell** | The XGrid Avalonia desktop application. |
| **Snapshot** | The core's stored machine state; exposed as slots 1–4 and `xgrid-session`. |
| **Surface scale** | Multiplier for the core's internal rendering resolution. |
| **Title ID** | The XBE certificate identifier used to key per-title overrides, favorites and play time. |
| **XBE** | Xbox executable format. |
| **XDVDFS** | The Xbox disc file system used by ISO/XISO and synthesized ZArchive file trees. |
| **XGD** | Xbox Game Disc format; retail images place the game partition at an XGD offset. |
| **XISO** | See ISO/XISO. |
| **ZArchive / ZAR** | A container format; XGrid mounts entry-form archives directly and synthesizes a volume for file-tree archives. |
