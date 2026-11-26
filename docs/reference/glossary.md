# Glossary

| Term | Meaning |
| --- | --- |
| **APU** | Audio Processing Unit in the MCPX chip; hosts the voice processor and DSP. |
| **AV pack** | The video output type the console reports to the guest (HDTV, composite, SCART, VGA, …). |
| **CHD** | Compressed Hunks of Data, a compressed disc-image format. |
| **Core** | The native xemu 0.8.139 emulator built from `xemu/`. |
| **DSP** | The guest audio microcode executed by the APU. |
| **EEPROM** | 256-byte console configuration storage. |
| **Frame sink** | Shared-memory path used by headless runs to count rendered frames. The shell never presents it. |
| **HRTF** | Head-related transfer function; spatial audio filtering. |
| **In-process** | Hosting mode where the core library runs inside the shell process. |
| **Input sink** | Shared-memory path where the shell publishes the player-1 pad. |
| **ISO/XISO** | An XDVDFS disc image; optimized or full Redump. |
| **CCI** | Cerbios/Repackinator compressed Xbox disc image. |
| **MCPX** | The Xbox southbridge/multimedia chip; also the boot ROM. |
| **NV2A** | The Xbox GPU. |
| **QEMU TCG** | Tiny Code Generator, the dynamic translation CPU core used by xemu. |
| **Save state** | A QEMU/xemu snapshot of the machine stored in the HDD image. |
| **Separate process** | Hosting mode where the core runs as `xemu.exe` beside the shell. |
| **Shell** | The XGrid Avalonia desktop application. |
| **Surface scale** | Multiplier for the core's internal rendering resolution. |
| **Title ID** | The XBE certificate identifier used to key per-title overrides. |
| **XBE** | Xbox executable format. |
| **XISO** | See ISO/XISO. |
| **ZArchive / ZAR** | A container format; XGrid can mount entry-form archives with an `.iso` entry. |
