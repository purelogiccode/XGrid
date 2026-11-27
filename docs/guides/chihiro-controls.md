# Chihiro controls

The Chihiro cabinet uses its own keyboard, mouse, gamepad and light-gun mapping.
The Xbox pad map does not apply to a Chihiro launch. Configure the cabinet under
**Settings → Input → Sega Chihiro cabinet keys**.

> Click the game picture once after launch. The embedded core takes keyboard
> focus for the cabinet (the Xbox path keeps focus in the shell for its pad
> sink). Click the toolbar or a shell page to return focus to XGrid.

## Default cabinet controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Start | `Enter` | Start |
| Service | `L` | L3 (left stick click) |
| Coin | `5` | Back |
| Test | `K` | R3 (right stick click) |
| Steer left / right | `Left` / `Right` | Left stick X |
| Gas / brake | `Up` / `Down` | RT / LT |
| Gear up / down | `]` / `[` | D-pad up / down |
| View change | (game-specific) | Y |
| Card in | `C` | X |
| Gun trigger / body | (game-specific) | RT / A |
| Gun change / reload | (game-specific) | B |

Press **5** for a coin and **Enter** to start after the cabinet has focus.

## Player 2 in gun cabinets

Player 2 uses a second gamepad or the **Player 2** rows in the same Settings
page.

| Action | Keyboard | Second gamepad |
| --- | --- | --- |
| Start | keypad Enter | Start |
| Coin | keypad 2 | Back |
| Card in | `V` | X |
| Aim | arrow keys | Right stick |
| Trigger | (bindable) | RT |
| Body | (bindable) | A |
| Change / pedal | (bindable) | B |
| Reload | (bindable) | LT |

The arrow keys move player 2's crosshair at the **Player 2 aim speed**; 100%
crosses the picture in about 0.75 seconds. The P2 gun trigger, body,
change/pedal and reload keys are unbound by default because the second gamepad
supplies them; bind them for a keyboard-only second gun. The aim keys only apply
to the gun profiles, so driving games still use the arrows for player 1.

## Cabinet profiles

The core selects the cabinet profile from the game's `boot.id`/executable:

- The House of the Dead III, Virtua Cop 3 and Ghost Squad use the gun layout.
- Crazy Taxi High Roller, OutRun 2 and Maximum Tune use the driving layout.
- Ollie King and Gundam have their own layouts.

The first detected gamepad drives the cabinet; a second gamepad aims player 2's
gun in the gun games. A wheel that SDL exposes as a gamepad works through its
stick and triggers.

If a homebrew or renamed executable is not recognized, choose its cabinet under
**Undetected game** in the cabinet keys. The core still auto-detects games in its
cabinet table; the selection is only the fallback.

## Gundam cockpit

Gundam Battle Operating Simulator's twin-stick cockpit is under **Gundam
cockpit** in Settings:

| Action | Keyboard |
| --- | --- |
| Left stick | `W` / `S` / `A` / `D` |
| Left trigger / button | `Q` / `E` |
| Right stick | `Up` / `Down` / `Left` / `Right` |
| Right trigger / button | `Right Shift` / `Right Ctrl` |
| Pedal | `Space` |

These bindings are written to `[chihiro.jvs.gundam]` at launch.

## Light guns and the mouse

In the gun games, move the host cursor over the picture to aim player 1; the
left mouse button fires, the right button is the body/action or reload button,
and RT/A/B do the same on a gamepad. Moving off the picture reports the
off-screen/reload position. Player 2 aims with the second gamepad's right stick
or the arrow keys.

The shell cursor is still visible, so use fullscreen or a large picture for the
best aim. In gun games the mouse buttons belong to the gun, so the core's pop-up
menu stays on **F2** (or the Guide button) and a double-click does not toggle
fullscreen.

### Separate gun pointers

On Windows, Linux and macOS, turn on **Separate gun pointers** in **Settings →
System → Sega Chihiro** to read each light gun or mouse on its own.

- Absolute light guns are preferred: the first aims player 1 and the second
  player 2.
- Their left button fires, the right is body, the middle is change or pedal, and
  the side buttons reload.
- Relative mice are tracked separately too, starting from the centre of the
  picture.
- Windows reads them through Raw Input, Linux through evdev
  (`/dev/input/event*`) and macOS through IOHID.

With the setting off, the host cursor and second-gamepad paths above still
apply.

Use **Player 1 pointer** and **Player 2 pointer** to pin a device by name when
automatic order picks the wrong one. Launch a gun game once with the setting on,
choose **Refresh** to read the core's pointer list, then select the gun for each
player. Automatic keeps the absolute-first order; a pinned device wins over it.

On Linux the user must be able to read `/dev/input/event*` (usually the `input`
group). On macOS allow Input Monitoring if the system asks. macOS releases
currently ship the shell only, so use a core built from this tree or point
**Emulator binary** at one.

## Card readers

Ghost Squad and Maximum Tune use the emulated card readers. Press **C** (or X) to
push player 1's card in hand into the reader, and **V** (or X on the second pad)
for player 2; with no card it creates a blank one. The core keeps the card with
the cabinet's card files, so a later session picks it up again. Ghost Squad's
two-player card screen uses both readers, one per player.

The readers and their card files are under **Settings → System → Sega Chihiro →
Card files**:

| Slot | Reader |
| --- | --- |
| Ghost Squad player 1 / player 2 | HW210 slot 1 / slot 2 |
| Gundam B.O.S. | The Gundam HW210 slot |
| Maximum Tune 1 / 2 | CRP-1231 MT1 / MT2 |

An empty slot creates a blank card in the core's `cards` folder the first time a
game asks for one. Choose a `.bin` card to keep a specific card; the game writes
its progress back to that file. **Card readers** turns both readers off for games
that should not see them. **Open cards folder** opens the folder where the core
creates blank cards.

## Force feedback

With **Force feedback** on, the core maps the drive board's effects to an SDL
haptic wheel:

- OutRun 2's Sega 838 spring, road resistance and event packages.
- Maximum Tune's V257 centering, damper and directional pushes.
- The Sega 838 vibration channel as a periodic buzz.

**Feedback strength** scales everything. **Wheel weight** is the share of the
drive board's damper the wheel applies. **Autocenter** and **Autocenter
strength** center the wheel while the game is not driving it. **Invert
direction** flips the push if the wheel pulls away from centre. With no
force-feedback wheel, the event channel rumbles the first gamepad instead.

## Related pages

- [Sega Chihiro setup](chihiro.md) — firmware, conversion, linked cabinets and
  limits.
- [Controllers and input](input.md) — the Xbox pad map and XInput setup.
- [Settings](settings.md) — the full Settings reference.
