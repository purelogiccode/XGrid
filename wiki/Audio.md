# Audio

The core emulates the MCPX APU, voice processor and guest DSP. XGrid exposes the
host sink and mixer options.

## Backends

| Backend | Platform | Behavior |
| --- | --- | --- |
| Auto | all | Let the core choose. |
| DirectSound | Windows | Explicit DirectSound. |
| None | all | No host output. |
| WAV capture | all | Writes `audio.wav` in the core working directory. |

## DSP and mixing

- **Guest DSP** — runs the APU GP/EP microcode; required for game audio.
- **DSP JIT** — native DSP code generation; off keeps the interpreter.
- **HRTF** — head-related filtering for 3D voices.
- **Volume limit** — caps the mixed output.
- **Voice workers** — 0 = auto.

## No sound?

1. Confirm the backend is not **None**.
2. Turn **Guest DSP** on.
3. Check the system output device.
4. Check **Activity** and `<data>/xemu/xemu.log`.
5. Try another backend.

Crackling usually means host CPU load; lower the resolution scale or close
background work.
