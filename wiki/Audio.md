# Audio

The core emulates the MCPX APU, voice processor and guest DSP. XGrid exposes the
host sink, backend and output device.

## Backends

The list is read from the core, so an uncompiled backend is not offered.

| Backend | Platform |
| --- | --- |
| Auto (recommended) | all |
| DirectSound | Windows |
| Core Audio | macOS |
| PulseAudio, ALSA, JACK, PipeWire, OSS, sndio | Linux/BSD |
| SDL | builds that compile it |
| Null (no output) | all |
| WAV capture | all |

Null discards host audio; headless runs use it automatically. WAV capture writes
`<data>/xemu/audio.wav`.

## Output device

- DirectSound lists playback devices with **Refresh**. A missing device falls
  back to the system default with a warning.
- PulseAudio, PipeWire, ALSA, OSS, sndio and JACK accept an optional device or
  server name.
- Auto, Core Audio, SDL, Null and WAV use the system default.

The device name cannot contain a comma.

## DSP and mixing

| Setting | Default | Meaning |
| --- | --- | --- |
| Guest DSP | on | Runs the APU microcode. Required for game audio. |
| DSP JIT | off | Native code generation for the DSP. |
| HRTF | on | Head-related filtering for 3D voices. |
| Volume limit | 1.0 | Caps mixed output. |
| Voice workers | 0 | Voice-processor workers; 0 = auto. |

## Troubleshooting

- No sound: backend not Null, Guest DSP on, output device working.
- Crackling: lower resolution scale, close background work, try another backend.
- Empty WAV: guest produced no audio, or DSP is off.
- Chihiro game silent: known upstream xemu Chihiro limitation.

Full guide: [Audio](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/audio.md).
