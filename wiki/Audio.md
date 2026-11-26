# Audio

The core emulates the MCPX APU, voice processor and guest DSP. XGrid exposes
the host sink, backend and output device.

## Backends

The settings list is read from the core's own `-audio-drivers` report, so a
backend the binary did not compile is never offered.

| Backend | Platform | Behavior |
| --- | --- | --- |
| Auto | all | Let the core choose. |
| DirectSound | Windows | Explicit DirectSound. |
| Core Audio | macOS | Explicit Core Audio. |
| PulseAudio / ALSA / JACK / PipeWire / OSS / sndio | Linux/BSD | Explicit native backend. |
| SDL | builds that compile it | SDL audio. |
| Null (no output) | all | Discard host audio; the guest keeps running. |
| WAV capture | all | Writes `audio.wav` in the core working directory. |

## Output device

- **DirectSound** lists the host's playback devices; **System default** follows
  Windows. A missing saved device falls back to the system default with a
  warning in **Activity**.
- **PulseAudio**, **PipeWire**, **ALSA**, **OSS**, **sndio** and **JACK** accept
  an optional device or server name; empty uses the system default.
- **Auto**, **Core Audio**, **SDL**, **Null** and **WAV capture** use the system
  default.

A device name cannot contain a comma (it separates QEMU audio options).

## Null sink

**Null (no output)** is QEMU's null sink. Headless `--headless` and `--benchmark`
runs select it automatically unless `--audio` selects another backend.

## DSP and mixing

- **Guest DSP** - runs the APU GP/EP microcode; required for game audio.
- **DSP JIT** - native DSP code generation; off keeps the interpreter.
- **HRTF** - head-related filtering for 3D voices.
- **Volume limit** - caps the mixed output.
- **Voice workers** - 0 = auto.

## No sound?

1. Confirm the backend is not **Null**.
2. Turn **Guest DSP** on.
3. Check the system output device and, for DirectSound, the selected device.
4. Check **Activity** and `<data>/xemu/xemu.log`.
5. Try another backend.

Crackling usually means host CPU load; lower the resolution scale or close
background work.
