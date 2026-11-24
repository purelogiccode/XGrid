# Audio

The core emulates the Xbox MCPX APU, its voice processor and the guest DSP
microcode. XGrid exposes the host sink and mixer options; the emulation itself is
part of the core.

## Host audio backends

| Backend | Platform | Behavior |
| --- | --- | --- |
| **Auto** | all | Let the core choose the best available host device. |
| **DirectSound** | Windows | Use DirectSound explicitly. |
| **None** | all | No host output. The guest still runs. |
| **WAV capture** | all | Write the mixed output to `audio.wav` in the core working directory. |

WAV capture is useful for diagnostics and content capture. It is not a
replacement for live audio.

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

with `<data>` being the per-user data folder. The file is created when the core
starts with the WAV backend.

## Troubleshooting

| Problem | Check |
| --- | --- |
| No sound at all | Backend is not **None**; **Guest DSP** is on; system output device works. |
| Sound in menus but not in game | The title may not initialize audio yet, or compatibility is incomplete. |
| Crackling or stutter | Host CPU load; lower resolution scale, close background work, try another backend. |
| WAV file is empty | The guest produced no audio yet, or the DSP did not run. Check the Activity log and the core log. |
| Audio after loading a state | A save state restores the audio device state; wait a moment for the core to run. |

The core's own log (`<data>/xemu/xemu.log` in separate-process mode) contains
audio backend errors.
