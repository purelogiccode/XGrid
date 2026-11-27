# Audio

The core emulates the Xbox MCPX APU, its voice processor and the guest DSP
microcode. XGrid exposes the host sink, backend and output device; the
emulation itself is part of the core.

## Host audio backends

The settings list is read from the core's own audio driver report, so a backend
the binary did not compile is never offered.

| Backend | Platform | Behavior |
| --- | --- | --- |
| **Auto (recommended)** | all | Let the core use its first compiled backend. |
| **DirectSound** | Windows | Use DirectSound explicitly. |
| **Core Audio** | macOS | Use Core Audio explicitly. |
| **PulseAudio** | Linux/BSD | Use PulseAudio. |
| **ALSA** | Linux | Use ALSA. |
| **JACK** | Linux/BSD | Use a JACK server. |
| **PipeWire** | Linux | Use PipeWire. |
| **OSS** | Linux/BSD | Use OSS. |
| **sndio** | Linux/BSD | Use sndio. |
| **SDL** | builds that compile it | Use SDL audio. |
| **Null (no output)** | all | Discard host audio. The guest still runs. |
| **WAV capture** | all | Write the mixed output to `audio.wav` in the core working directory. |

WAV capture is useful for diagnostics and content capture. It is not a
replacement for live audio.

## Output device

The device row depends on the backend:

- **DirectSound** lists the host's playback devices with a **Refresh** button.
  **System default** follows Windows. The setting stores the device GUID; if the
  device is unplugged before launch, XGrid logs a warning and falls back to the
  system default instead of failing the core.
- **PulseAudio**, **PipeWire**, **ALSA**, **OSS**, **sndio** and **JACK** accept
  an optional backend-specific device or server name. Empty uses the system
  default.
- **Auto**, **Core Audio**, **SDL**, **Null** and **WAV capture** use the system
  default and do not take a device.

The device name cannot contain a comma; QEMU uses commas to separate audio
options.

## Null sink and headless runs

**Null (no output)** is QEMU's null sink. It decodes and mixes the guest audio
but discards it, so the console runs at full speed without an output device.
Headless `--headless` / `--benchmark` runs select the null sink automatically
unless `--audio` explicitly selects another backend.

## DSP and mixing

| Setting | Default | Meaning |
| --- | --- | --- |
| **Guest DSP** | on | Runs the APU GP/EP microcode. Required for game audio. |
| **DSP JIT** | off | Native code generation for the DSP. Off keeps the interpreter. |
| **HRTF** | on | Head-related filtering for 3D voices. |
| **Volume limit** | 1.0 | Caps the mixed output. |
| **Voice workers** | 0 | Voice-processor worker count; 0 = auto. |

If audio is silent, first confirm **Guest DSP** is on.

## Where the WAV file goes

```text
<data>/xemu/audio.wav
```

The file is created when the core starts with the WAV backend. The core uses
`<data>/xemu/` as its working directory.

## Troubleshooting

| Problem | Check |
| --- | --- |
| No sound at all | Backend is not **Null**; **Guest DSP** is on; system output device works. |
| Sound in menus but not in game | The title may not initialize audio yet, or compatibility is incomplete. |
| Crackling or stutter | Host CPU load; lower resolution scale, close background work, try another backend. |
| DirectSound device is missing | Power off, open **Settings → Audio**, refresh the device list and pick an attached device, or use **System default**. |
| Backend refused at launch | The core build did not compile that backend. Pick one from the Settings list; it is read from the core's report. |
| WAV file is empty | The guest produced no audio yet, or the DSP did not run. Check the Activity log and the core log. |
| Audio after loading a state | A save state restores the audio device state; wait a moment for the core to run. |
| Chihiro game is silent | Known upstream xemu Chihiro limitation; the Xbox audio path is unaffected. |

The core's own log (`<data>/xemu/xemu.log` in separate-process mode) contains
audio backend errors. See [Logs and diagnostics](diagnostics.md).
