# xemu open issues - UI and general emulation

Filtered list of open [xemu](https://github.com/xemu-project/xemu/issues) issues that concern the emulator shell/UI or general emulation behaviour.

- **Snapshot:** 2026-11-24
- **Source:** https://github.com/xemu-project/xemu/issues
- **Scope:** 855 open issues and 102 open pull requests at snapshot time; PRs are excluded.
- **Kept:** shell/UI, input, display, snapshots/configuration, and general emulation (CPU, GPU/NV2A, APU, storage, networking, video output), plus general host compatibility/performance.
- **Excluded:** game-specific reports (a particular title's graphics, shaders, performance, audio, or crashes), compatibility lists, and build/CI/docs-only issues.

## Summary

| Section | Issues |
| --- | ---: |
| UI and shell | 54 |
| Input and controllers | 22 |
| Display, aspect ratio and frame pacing | 23 |
| Snapshots, save data and configuration | 16 |
| General emulation - CPU, memory and chipset | 14 |
| General emulation - GPU / NV2A | 65 |
| General emulation - audio / APU | 17 |
| General emulation - storage, networking and video output | 24 |
| General performance, crashes and host compatibility | 55 |
| General meta / tracking issues | 8 |
| **Total** | **298** |

## UI and shell

- [#3011](https://github.com/xemu-project/xemu/issues/3011) - Notify user when running on an incompatible system
- [#2971](https://github.com/xemu-project/xemu/issues/2971) - Render Xemu at the guest FPS while the GUI isn't active
- [#2852](https://github.com/xemu-project/xemu/issues/2852) - Could not select file location under System->Files using xemu installed on Archlinux Hyprland window manager
- [#2832](https://github.com/xemu-project/xemu/issues/2832) - Ability to use more than one Games Directory
- [#2762](https://github.com/xemu-project/xemu/issues/2762) - Cannot select detailed settings with controller
- [#2760](https://github.com/xemu-project/xemu/issues/2760) - Screen Reader Accessibility Support for UI
- [#2544](https://github.com/xemu-project/xemu/issues/2544) - Recent files tab
- [#2505](https://github.com/xemu-project/xemu/issues/2505) - Add a easier way to clear cache
- [#2307](https://github.com/xemu-project/xemu/issues/2307) - Disable VK on unsupported systems
- [#2269](https://github.com/xemu-project/xemu/issues/2269) - Validate input files are in xiso format when opening and provide a friendly message if not
- [#2257](https://github.com/xemu-project/xemu/issues/2257) - Auto updater fails when xemu is located in a path with accents
- [#2255](https://github.com/xemu-project/xemu/issues/2255) - Loading images via URLs does not work on Windows
- [#2188](https://github.com/xemu-project/xemu/issues/2188) - Windows Auto updater reverting disc inserted like xemu crashed
- [#2074](https://github.com/xemu-project/xemu/issues/2074) - Display a games list if the dashboard is not present
- [#2034](https://github.com/xemu-project/xemu/issues/2034) - accessibility with screen reader software
- [#1928](https://github.com/xemu-project/xemu/issues/1928) - Check if xemu is writing to hdd before closing
- [#1843](https://github.com/xemu-project/xemu/issues/1843) - Add Additional Command Line Arguments for Settings Such as "Backend", and "Internal Resolution Scale"
- [#1812](https://github.com/xemu-project/xemu/issues/1812) - Less But More Informative CLI `--help` Text
- [#1808](https://github.com/xemu-project/xemu/issues/1808) - Xemu requires reset after loading ISO via QMP.
- [#1791](https://github.com/xemu-project/xemu/issues/1791) - volume presets for games
- [#1729](https://github.com/xemu-project/xemu/issues/1729) - Per game settings
- [#1716](https://github.com/xemu-project/xemu/issues/1716) - Add option to cold reset
- [#1715](https://github.com/xemu-project/xemu/issues/1715) - Let `-dvd_path` update xemu config
- [#1714](https://github.com/xemu-project/xemu/issues/1714) - Move RenderDoc build option to runtime option
- [#1713](https://github.com/xemu-project/xemu/issues/1713) - Include debug info in screenshots
- [#1671](https://github.com/xemu-project/xemu/issues/1671) - CPU model may not be displayed in System Information section
- [#1563](https://github.com/xemu-project/xemu/issues/1563) - Request: Option to open the emulator without automatically running the last game
- [#1483](https://github.com/xemu-project/xemu/issues/1483) - Make headless mode work again
- [#1457](https://github.com/xemu-project/xemu/issues/1457) - Show nice background when in powered-off state or when display is uninitialized
- [#1456](https://github.com/xemu-project/xemu/issues/1456) - Support staying open while VM is powered-off state
- [#1454](https://github.com/xemu-project/xemu/issues/1454) - Options for the update system
- [#1423](https://github.com/xemu-project/xemu/issues/1423) - Pause emulation when out of focus + Block screen saver
- [#1419](https://github.com/xemu-project/xemu/issues/1419) - Power on by Eject button, for ndure xploit
- [#1355](https://github.com/xemu-project/xemu/issues/1355) - Integrated EEPROM editor
- [#1350](https://github.com/xemu-project/xemu/issues/1350) - Add auto update support for macOS releases
- [#1349](https://github.com/xemu-project/xemu/issues/1349) - Add auto update support for appimage releases
- [#1329](https://github.com/xemu-project/xemu/issues/1329) - Add crash reporting
- [#1316](https://github.com/xemu-project/xemu/issues/1316) - Popup menu may continually re-open
- [#1310](https://github.com/xemu-project/xemu/issues/1310) - [Feature Request] Built-in language + region + video standard EEPROM settings per game
- [#1268](https://github.com/xemu-project/xemu/issues/1268) - Retain screen position / target screen in xemu.toml and restore when starting the program
- [#1132](https://github.com/xemu-project/xemu/issues/1132) - Monitor recalls typed commands after xemu is restarted.
- [#1128](https://github.com/xemu-project/xemu/issues/1128) - Add user interface for attaching XMU, XBLC
- [#1000](https://github.com/xemu-project/xemu/issues/1000) - Add render scale to about window (and compatibility reports)
- [#969](https://github.com/xemu-project/xemu/issues/969) - Windows post-update relaunch can fail
- [#900](https://github.com/xemu-project/xemu/issues/900) - Check for updates option only does something on Windows
- [#774](https://github.com/xemu-project/xemu/issues/774) - `screendump` has cached image of Xbox logo
- [#522](https://github.com/xemu-project/xemu/issues/522) - macOS 'receive keystrokes from any application' popup
- [#504](https://github.com/xemu-project/xemu/issues/504) - Non-ASCII characters are not displayed properly
- [#465](https://github.com/xemu-project/xemu/issues/465) - Benchmark Mode: Recording FPS and MSPF values and providing Averages
- [#377](https://github.com/xemu-project/xemu/issues/377) - Xemu debug build updates to non debug
- [#370](https://github.com/xemu-project/xemu/issues/370) - Show proper error message when failing to initialize
- [#362](https://github.com/xemu-project/xemu/issues/362) - Configurable shotcuts / hotkey rebinding for emulator actions
- [#150](https://github.com/xemu-project/xemu/issues/150) - Please add Chinese language packs（And more languages）
- [#160](https://github.com/xemu-project/xemu/issues/160) - Feature Request: LED Status

## Input and controllers

- [#3082](https://github.com/xemu-project/xemu/issues/3082) - For Keyboard Input, allow Mouse movement and buttons to be bound, please
- [#2856](https://github.com/xemu-project/xemu/issues/2856) - Add more verbose SDL Logging/HID Enumeration
- [#2796](https://github.com/xemu-project/xemu/issues/2796) - Bluetooth Gamepads not working on 0.8.134 (or 0.8.133)
- [#2653](https://github.com/xemu-project/xemu/issues/2653) - 360Controller driver working everywhere else but not in xemu? [macOS]
- [#2634](https://github.com/xemu-project/xemu/issues/2634) - Flatpak does not recognize USB controller, AppImage works normally
- [#2624](https://github.com/xemu-project/xemu/issues/2624) - Changing controller can break the vm snapshots (Error -22)
- [#2286](https://github.com/xemu-project/xemu/issues/2286) - Add gyro aiming support
- [#1874](https://github.com/xemu-project/xemu/issues/1874) - Hall Effect Sticks dont read Diagonals at full range
- [#1717](https://github.com/xemu-project/xemu/issues/1717) - Input not yet configured when loading snapshots from command line
- [#1572](https://github.com/xemu-project/xemu/issues/1572) - XMU Devices are not preserved when switching between controllers on an input
- [#1518](https://github.com/xemu-project/xemu/issues/1518) - Xbox controller recognized but not working
- [#1484](https://github.com/xemu-project/xemu/issues/1484) - Bind inputs on startup before loadvm
- [#1274](https://github.com/xemu-project/xemu/issues/1274) - Input not working in Cromwell BIOS
- [#1244](https://github.com/xemu-project/xemu/issues/1244) - Failed to initialize SDL gamecontroller subsystem: Could not initialize UDEV
- [#1239](https://github.com/xemu-project/xemu/issues/1239) - Passed Through USB devices do not match hardware behavior
- [#1234](https://github.com/xemu-project/xemu/issues/1234) - USB pass through
- [#946](https://github.com/xemu-project/xemu/issues/946) - xemu: UI always accepts xinput even when controller is set to none
- [#746](https://github.com/xemu-project/xemu/issues/746) - Improve controller input latency
- [#606](https://github.com/xemu-project/xemu/issues/606) - [Feature Request] Support for pressure-sensitive face buttons
- [#389](https://github.com/xemu-project/xemu/issues/389) - Adding missing peripherals
- [#135](https://github.com/xemu-project/xemu/issues/135) - Possible duplicate joypad rumble requests?
- [#50](https://github.com/xemu-project/xemu/issues/50) - Force feedback isn't auto-disabled after closing input window during test

## Display, aspect ratio and frame pacing

- [#3134](https://github.com/xemu-project/xemu/issues/3134) - Fullscreen is always Exclusive Fullscreen regardless of settings
- [#2709](https://github.com/xemu-project/xemu/issues/2709) - Enumerate and allow user to select exclusive fullscreen mode
- [#2630](https://github.com/xemu-project/xemu/issues/2630) - Heavy stuttering with Freesync on
- [#2536](https://github.com/xemu-project/xemu/issues/2536) - Launching Xemu breaks HDR in Windows 11
- [#1876](https://github.com/xemu-project/xemu/issues/1876) - Black screen on full screen with W11 HDR enabled
- [#1798](https://github.com/xemu-project/xemu/issues/1798) - 3:2 screen aspect ratio
- [#1719](https://github.com/xemu-project/xemu/issues/1719) - Aspect ratio "auto" is not displaying the correct aspect for games that natively support 16:9
- [#1688](https://github.com/xemu-project/xemu/issues/1688) - A new widescreen that is a progressive stretch. (Similar to Wide Panoramic mode on my older Vizio TV)
- [#1470](https://github.com/xemu-project/xemu/issues/1470) - Automatic aspect ratio doesn't function as intended
- [#1354](https://github.com/xemu-project/xemu/issues/1354) - Option to disable output filtering
- [#1262](https://github.com/xemu-project/xemu/issues/1262) - "Enable Overscan" Toggle
- [#2073](https://github.com/xemu-project/xemu/issues/2073) - Anti-Aliasing, Filtering Options, and Displaying the Exact Resolution with Render Scale
- [#2195](https://github.com/xemu-project/xemu/issues/2195) - Add "transparent fps" in display menu.
- [#1260](https://github.com/xemu-project/xemu/issues/1260) - Simplified fps and mspf monitor
- [#1021](https://github.com/xemu-project/xemu/issues/1021) - Framerate Graph Inaccurate
- [#2521](https://github.com/xemu-project/xemu/issues/2521) - Framerate mointoring on 3rd party software
- [#269](https://github.com/xemu-project/xemu/issues/269) - Maximizing after launch causes dithering
- [#1541](https://github.com/xemu-project/xemu/issues/1541) - Video problem
- [#1011](https://github.com/xemu-project/xemu/issues/1011) - Framerate caps on xemu doesn't work correctly
- [#2399](https://github.com/xemu-project/xemu/issues/2399) - Adjustable framerate cap
- [#1018](https://github.com/xemu-project/xemu/issues/1018) - Allow for games with double buffer vsync to mantain 60 fps
- [#2076](https://github.com/xemu-project/xemu/issues/2076) - Internal render scale - hotkey
- [#1653](https://github.com/xemu-project/xemu/issues/1653) - Brightness adjustment within emulator settings

## Snapshots, save data and configuration

- [#3150](https://github.com/xemu-project/xemu/issues/3150) - Save data manager
- [#2284](https://github.com/xemu-project/xemu/issues/2284) - When saving snapshot with emulated memory card attached: Error: Device 'xmu_0' is writable but does not support snapshots
- [#2564](https://github.com/xemu-project/xemu/issues/2564) - Prevent accidental save overwrites by adding separate base-sections for save and load games in snapshot menu
- [#1566](https://github.com/xemu-project/xemu/issues/1566) - Snapshots bound to a key overwrite snapshots of other games
- [#1562](https://github.com/xemu-project/xemu/issues/1562) - Request: an easier way to load snapshots
- [#1543](https://github.com/xemu-project/xemu/issues/1543) - Crash when going to Snapshots settings (after creating an unnamed snapshot).
- [#1159](https://github.com/xemu-project/xemu/issues/1159) - VM states are not backward compatible
- [#1091](https://github.com/xemu-project/xemu/issues/1091) - Using Snapshots when the emulator pause lock up xemu (Windows Only)
- [#818](https://github.com/xemu-project/xemu/issues/818) - nv2a: Creating save states while the VM is paused does not preserve the paused frame
- [#1375](https://github.com/xemu-project/xemu/issues/1375) - Offer to save machine state on exit
- [#1670](https://github.com/xemu-project/xemu/issues/1670) - "Created new snapshot" notification may appear when it was not actually created
- [#1669](https://github.com/xemu-project/xemu/issues/1669) - Snapshot error displayed as modal dialog when system is not fully configured
- [#2730](https://github.com/xemu-project/xemu/issues/2730) - Make required file paths relative to xemu.toml file so they are portable across machines
- [#2480](https://github.com/xemu-project/xemu/issues/2480) - shaders/log file location should be bound to `-config_path`
- [#2549](https://github.com/xemu-project/xemu/issues/2549) - Auto change EEPROM region based on disk region
- [#139](https://github.com/xemu-project/xemu/issues/139) - [Feature Request] Game save management

## General emulation - CPU, memory and chipset

- [#3097](https://github.com/xemu-project/xemu/issues/3097) - FPU/SSE (and presumably MMX) behavior does not match HW
- [#2896](https://github.com/xemu-project/xemu/issues/2896) - Open source kernel integration
- [#2850](https://github.com/xemu-project/xemu/issues/2850) - Register LPC PM GPIO IO ports.
- [#1887](https://github.com/xemu-project/xemu/issues/1887) - Feature Request: Fast Forward / Turbo (Unlimited FPS)
- [#1686](https://github.com/xemu-project/xemu/issues/1686) - support for 256mb of max system memory
- [#1429](https://github.com/xemu-project/xemu/issues/1429) - [Feature Request] virtual cpu clock control
- [#2167](https://github.com/xemu-project/xemu/issues/2167) - Option to "overclock" emulated console for better performance
- [#2338](https://github.com/xemu-project/xemu/issues/2338) - PAL50 incompatibility
- [#2318](https://github.com/xemu-project/xemu/issues/2318) - Generated EEPROM may have unconventional serial number
- [#1328](https://github.com/xemu-project/xemu/issues/1328) - Improve flash emulation
- [#924](https://github.com/xemu-project/xemu/issues/924) - NV2A MMIO register memory is 4x as large as it should be
- [#638](https://github.com/xemu-project/xemu/issues/638) - Missing PCI devices
- [#164](https://github.com/xemu-project/xemu/issues/164) - [Feature Request] I/O Throttling
- [#1442](https://github.com/xemu-project/xemu/issues/1442) - Chihiro Emulation

## General emulation - GPU / NV2A

- [#3184](https://github.com/xemu-project/xemu/issues/3184) - Handling of exceptional values in vertex shaders is incorrect
- [#3179](https://github.com/xemu-project/xemu/issues/3179) - Programmable vertex shaders: point sprite size is not scaled by the resolution scale (sprites shrink at 2x/3x)
- [#3173](https://github.com/xemu-project/xemu/issues/3173) - `SET_POINT_SIZE` commands received within a BEGIN/END block should be ignored but are handled
- [#3170](https://github.com/xemu-project/xemu/issues/3170) - nv2a: Late NO_OPERATION trap interrupt wakes D3D fence wait early, pushbuffer gets overwritten ("Reserved pb command" assert)
- [#3118](https://github.com/xemu-project/xemu/issues/3118) - Attribute carryover for out-of-order DRAW_ARRAYS draws is incorrect
- [#3117](https://github.com/xemu-project/xemu/issues/3117) - Handling of UB_D3D attributes in `pgraph_update_inline_value` is incorrect (R & B swap)
- [#3092](https://github.com/xemu-project/xemu/issues/3092) - Point sprites are rendered upside down compared to HW
- [#3091](https://github.com/xemu-project/xemu/issues/3091) - Handling of `PS_COMBINEROUTPUT_BIAS` and `PS_COMBINEROUTPUT_SHIFTLEFT_1_BIAS` with SpecularR0Sum does not match HW
- [#2803](https://github.com/xemu-project/xemu/issues/2803) - GLSL floats are not suitable for VS emulation (Issue Upstream xqemu #78)
- [#2804](https://github.com/xemu-project/xemu/issues/2804) - Shader pre cache?
- [#2631](https://github.com/xemu-project/xemu/issues/2631) - Async Shader
- [#2746](https://github.com/xemu-project/xemu/issues/2746) - Use lmdb for shader cache
- [#1725](https://github.com/xemu-project/xemu/issues/1725) - Add shader cache quota
- [#2604](https://github.com/xemu-project/xemu/issues/2604) - Make shader gen locale independent
- [#1337](https://github.com/xemu-project/xemu/issues/1337) - Texture injection for 4k texture packs like pcsx2 etc.
- [#2334](https://github.com/xemu-project/xemu/issues/2334) - Res Multiplier spacing Fix
- [#2323](https://github.com/xemu-project/xemu/issues/2323) - nv2a/vk: Border textures are incorrectly allocated
- [#2311](https://github.com/xemu-project/xemu/issues/2311) - nv2a/gl: GL_UNPACK_ALIGNMENT may be incorrect on texture upload
- [#2175](https://github.com/xemu-project/xemu/issues/2175) - HW rounding bug Breaks Tile rendering  Above 1x resolution
- [#2174](https://github.com/xemu-project/xemu/issues/2174) - HW rounding 0.5 offset causing graphical issue on shadows above 1x resolution
- [#2260](https://github.com/xemu-project/xemu/issues/2260) - NV097_SET_COLOR_KEY_COLOR interaction with indexed/only-alpha/low-component texture formats needs investigation
- [#2113](https://github.com/xemu-project/xemu/issues/2113) - Option to force texture mipmapping
- [#1990](https://github.com/xemu-project/xemu/issues/1990) - Various games lack shadow filtering making them look very pixilated
- [#1914](https://github.com/xemu-project/xemu/issues/1914) - Handling of NV097_SET_POINT_PARAMS is incorrect
- [#1837](https://github.com/xemu-project/xemu/issues/1837) - Vulkan: Frame scaling/resolution issues causing distortion in some games
- [#1833](https://github.com/xemu-project/xemu/issues/1833) - Vulkan: Broken Texture Filtering on some games
- [#2470](https://github.com/xemu-project/xemu/issues/2470) - Handling of `PS_TEXTUREMODES_DOT_STR_3D` differs from HW
- [#2469](https://github.com/xemu-project/xemu/issues/2469) - Handling of `PS_TEXTUREMODES_DOT_RFLCT_SPEC_CONST` is unimplemented
- [#2468](https://github.com/xemu-project/xemu/issues/2468) - Handling of `dotmap_hilo_hemisphere` DOT_RGBMAPPING differs from HW
- [#2467](https://github.com/xemu-project/xemu/issues/2467) - Handling of `PS_TEXTUREMODES_BUMPENVMAP_LUM` differs from HW
- [#2466](https://github.com/xemu-project/xemu/issues/2466) - Handling of `PS_TEXTUREMODES_DOT_ZW` is incorrect
- [#2464](https://github.com/xemu-project/xemu/issues/2464) - Handling of pixel shader modes with texture stage disabled via `NV097_SET_TEXTURE_CONTROL0` differs from HW
- [#2408](https://github.com/xemu-project/xemu/issues/2408) - Class 0x19 clip rectangle support for 2d blitting operations is not implemented
- [#587](https://github.com/xemu-project/xemu/issues/587) - "`NV_PGRAPH_TEXFILTER_SIGNED` not supported" - leads to incorrect looking textures
- [#576](https://github.com/xemu-project/xemu/issues/576) - Rendered text glyphs apparently offset in various titles When Upscaling
- [#551](https://github.com/xemu-project/xemu/issues/551) - Handling of infinite z-clip max in floating point z-buffer modes differs from hardware
- [#34](https://github.com/xemu-project/xemu/issues/34) - Textures may extend beyond TOM
- [#204](https://github.com/xemu-project/xemu/issues/204) - Investigate write enable gate, specifically clear
- [#1005](https://github.com/xemu-project/xemu/issues/1005) - Handling of textures with address mode CLAMP_OGL is incorrect
- [#1004](https://github.com/xemu-project/xemu/issues/1004) - Handling of textures with borders is incorrect (i.e., not `NV_PGRAPH_TEXFMT0_BORDER_SOURCE_COLOR`)
- [#1010](https://github.com/xemu-project/xemu/issues/1010) - Some Games Shadow are streched
- [#1038](https://github.com/xemu-project/xemu/issues/1038) - Convolution texture filter (MAG interpolation) not implemented
- [#843](https://github.com/xemu-project/xemu/issues/843) - Depth values generated in fixed function pipeline with modified `SET_CLIP_MIN` / `SET_CLIP_MAX` lead to incorrect depth buffer values
- [#775](https://github.com/xemu-project/xemu/issues/775) - nv2a: Warning unimplemented feature: Same color & zeta surface offset
- [#763](https://github.com/xemu-project/xemu/issues/763) - Improve gfx resource deallocation
- [#715](https://github.com/xemu-project/xemu/issues/715) - nv2a 0x97 -> 0x1480 - 0x14fc (3D_POLYGON_STIPPLE_PATTERN?) unhandled
- [#711](https://github.com/xemu-project/xemu/issues/711) - nv2a 0x97 -> 0x1d84 (NV097_SET_OCCLUDE_ZSTENCIL_EN) unhandled
- [#710](https://github.com/xemu-project/xemu/issues/710) - nv2a 0x97 -> 0x1d80 (NV097_SET_COMPRESS_ZBUFFER_EN) unhandled
- [#709](https://github.com/xemu-project/xemu/issues/709) - nv2a 0x97 -> 0x1d78 (glEnable(GL_DEPTH_CLAMP)?) unhandled
- [#708](https://github.com/xemu-project/xemu/issues/708) - nv2a 0x97 -> 0x17c4 (glLightModeli(GL_LIGHT_MODEL_TWO_SIDE, GL_TRUE)?) unhandled
- [#707](https://github.com/xemu-project/xemu/issues/707) - nv2a 0x97 -> 0x1710 (3D_VTXBUF_VALIDATE?) unhandled
- [#706](https://github.com/xemu-project/xemu/issues/706) - nv2a 0x97 -> 0x16bc (glEdgeFlag?) unhandled
- [#705](https://github.com/xemu-project/xemu/issues/705) - nv2a 0x97 -> 0x147c (glEnable(GL_POLYGON_STIPPLE)?) unhandled
- [#704](https://github.com/xemu-project/xemu/issues/704) - nv2a 0x97 -> 0x09fc (NV097_SET_FLAT_SHADE_OP) unhandled
- [#702](https://github.com/xemu-project/xemu/issues/702) - nv2a 0x97 -> 0x09f8 (NV097_SET_SWATH_WIDTH) unhandled
- [#698](https://github.com/xemu-project/xemu/issues/698) - nv2a 0x97 -> 0x0380 (glLineWidth?) unhandled
- [#667](https://github.com/xemu-project/xemu/issues/667) - Incorrect shading when using a shader with explicit w-params
- [#602](https://github.com/xemu-project/xemu/issues/602) - Bug: Issue with scaling some objects and or textures
- [#936](https://github.com/xemu-project/xemu/issues/936) - Antialiasing can trigger erroneous surface overlap detection on tightly packed surfaces
- [#1238](https://github.com/xemu-project/xemu/issues/1238) - pgraph_bind_textures: Assertion `levels > 0' failed.
- [#1471](https://github.com/xemu-project/xemu/issues/1471) - PGRAPH dirty surface updates corrupting memory
- [#572](https://github.com/xemu-project/xemu/issues/572) - FMV have green Encoding bars (Floating point issue)
- [#739](https://github.com/xemu-project/xemu/issues/739) - Startup Xbox logo animation shows corner gaps
- [#409](https://github.com/xemu-project/xemu/issues/409) - Anisotropic filtering option
- [#312](https://github.com/xemu-project/xemu/issues/312) - Some titles have scissor issues

## General emulation - audio / APU

- [#3066](https://github.com/xemu-project/xemu/issues/3066) - apu: emulator locks up in peek_ahead_multipass_bin when guest voice list is cyclic
- [#2901](https://github.com/xemu-project/xemu/issues/2901) - Surround + "EP Realtime" may cause xemu to hang
- [#2763](https://github.com/xemu-project/xemu/issues/2763) - Loss of Sound going from 0.8.133 to 0.8.134 on Windows ARM64
- [#2363](https://github.com/xemu-project/xemu/issues/2363) - [GNU/Linux] ALSA constanly having buffer underruns
- [#1868](https://github.com/xemu-project/xemu/issues/1868) - Latest update (v0.8.8) caused audio tears/lag
- [#2600](https://github.com/xemu-project/xemu/issues/2600) - Some games audio goes out of sync when changing renders
- [#2251](https://github.com/xemu-project/xemu/issues/2251) - audio can prevent apps from booting on retail bios debug bios bypasses issue
- [#1319](https://github.com/xemu-project/xemu/issues/1319) - write_memory_raw: assertion failed: ((value & 0xFF000000) == 0)
- [#1529](https://github.com/xemu-project/xemu/issues/1529) - NXDK XAudio works on real hardware but not under Xemu
- [#2331](https://github.com/xemu-project/xemu/issues/2331) - Audio/sound Null Backend Option/Feature
- [#371](https://github.com/xemu-project/xemu/issues/371) - Add menu setting to choose the audio backend
- [#1639](https://github.com/xemu-project/xemu/issues/1639) - No Audio output to headphones from pcie soundcard. HDMI audio works fine
- [#2457](https://github.com/xemu-project/xemu/issues/2457) - HDD and DVD Drive sound emulation
- [#435](https://github.com/xemu-project/xemu/issues/435) - Audio CD Support
- [#1465](https://github.com/xemu-project/xemu/issues/1465) - random mcpx failure loading unleashx dashboard
- [#380](https://github.com/xemu-project/xemu/issues/380) - [Assertion] segment_offset != 0
- [#332](https://github.com/xemu-project/xemu/issues/332) - [Assertion] address < DSP_YRAM_SIZE

## General emulation - storage, networking and video output

- [#1036](https://github.com/xemu-project/xemu/issues/1036) - XDVD security challenge responses Unimplemented
- [#2958](https://github.com/xemu-project/xemu/issues/2958) - Requiring file locks when loading discs prevents loading discs
- [#994](https://github.com/xemu-project/xemu/issues/994) - Xebian 1.1.4: Asserts soon after loading ISO because PIO Mode is not emulated
- [#1420](https://github.com/xemu-project/xemu/issues/1420) - HDD image file damaged after copy data from iso or ftp
- [#2581](https://github.com/xemu-project/xemu/issues/2581) - Xemu crashes when loaading a game level after swapping the disk
- [#449](https://github.com/xemu-project/xemu/issues/449) - pgraph.c DMA limit assertion when trying to swap disc while boot animation is playing
- [#392](https://github.com/xemu-project/xemu/issues/392) - nvnet ring index, size state vars possibly incorrect
- [#2305](https://github.com/xemu-project/xemu/issues/2305) - nvnet dosen't handle unaligned MMIO write access by cromwell
- [#2910](https://github.com/xemu-project/xemu/issues/2910) - network tx timeout with forcedeth driver
- [#2350](https://github.com/xemu-project/xemu/issues/2350) - UDP tunnel drops Xlink connections randomly for brief moments
- [#2168](https://github.com/xemu-project/xemu/issues/2168) - Windows TAP network broken since 0.8.6
- [#2120](https://github.com/xemu-project/xemu/issues/2120) - NAT networking breaks on autoupdater restart Windows
- [#2119](https://github.com/xemu-project/xemu/issues/2119) - NAT ip address range needs to be configurable
- [#1314](https://github.com/xemu-project/xemu/issues/1314) - Unable to access network adapter in bridged mode on Steam Deck
- [#1332](https://github.com/xemu-project/xemu/issues/1332) - AppImage/flatpak bridged adapter permissions
- [#546](https://github.com/xemu-project/xemu/issues/546) - [Feature Request] Netplay for xemu
- [#238](https://github.com/xemu-project/xemu/issues/238) - Compressed disc image support
- [#2395](https://github.com/xemu-project/xemu/issues/2395) - Play games from physical discs?
- [#2459](https://github.com/xemu-project/xemu/issues/2459) - Game Image ZIP File support
- [#1196](https://github.com/xemu-project/xemu/issues/1196) - Hack treating PVIDEO `size_in` of `0xFFFFFFFF` as end of overlay does not match HW behavior
- [#2434](https://github.com/xemu-project/xemu/issues/2434) - PVIDEO handling of NV_PVIDEO_SIZE_IN is incorrect
- [#2429](https://github.com/xemu-project/xemu/issues/2429) - PVIDEO buffer/overlay 1 is not implemented
- [#2465](https://github.com/xemu-project/xemu/issues/2465) - VGA output is broken (AV Pack = VGA)
- [#1680](https://github.com/xemu-project/xemu/issues/1680) - Some games have random blue or white border when playing trailers, extra content etc videos

## General performance, crashes and host compatibility

- [#3188](https://github.com/xemu-project/xemu/issues/3188) - On Going Vulkan Crash
- [#3183](https://github.com/xemu-project/xemu/issues/3183) - DXGI falling back to legacy mode.
- [#3104](https://github.com/xemu-project/xemu/issues/3104) - intel arc B580 black screen
- [#3083](https://github.com/xemu-project/xemu/issues/3083) - Idle on the settings screen, xemu (Flatpak, Intel/Mesa) leaks ~36 GB of GPU shared memory overnight
- [#3027](https://github.com/xemu-project/xemu/issues/3027) - Huge FPS drop when mouse cursor not visible (SteamOS Gaming Mode)
- [#3016](https://github.com/xemu-project/xemu/issues/3016) - Xemu version 0.8.136 appears to conflict with NVIDIA Game Ready driver 610.88
- [#2985](https://github.com/xemu-project/xemu/issues/2985) - v0.8.136 Will Not Launch on Intel Xeon X5690 (Silent Failure)
- [#2976](https://github.com/xemu-project/xemu/issues/2976) - Xemu launches, freezes then crashes
- [#2920](https://github.com/xemu-project/xemu/issues/2920) - xemu release build of 0.8.136 Not Launching on windows 10, debug build works
- [#2918](https://github.com/xemu-project/xemu/issues/2918) - Bypass OpenGL 4 check for Raspberry Pi 5
- [#2845](https://github.com/xemu-project/xemu/issues/2845) - Performance issue with DXGI swapchain and recent NVIDIA drivers in combination with unlocked UI thread (0.8.144 and later)
- [#2842](https://github.com/xemu-project/xemu/issues/2842) - High memory usage on Manjaro when idling and not playing a game (AppImage version)
- [#2841](https://github.com/xemu-project/xemu/issues/2841) - Total system freeze
- [#2770](https://github.com/xemu-project/xemu/issues/2770) - XEMU does not work AT ALL on WINDOW 11 machine.
- [#2723](https://github.com/xemu-project/xemu/issues/2723) - Clear/Invalidate Cache upon Assertion
- [#2651](https://github.com/xemu-project/xemu/issues/2651) - Emulator crash when try to change the render to vulkan
- [#2618](https://github.com/xemu-project/xemu/issues/2618) - Flatpak version fails when swtiching to Vulkan rendering
- [#2603](https://github.com/xemu-project/xemu/issues/2603) - [Vulkan] High VRAM usage
- [#2540](https://github.com/xemu-project/xemu/issues/2540) - Crashes On Startup
- [#2506](https://github.com/xemu-project/xemu/issues/2506) - [REGRESSION] macOS low in game FPS/pacing after 0.8.108-2-g54e4580efc
- [#2380](https://github.com/xemu-project/xemu/issues/2380) - V0.7.99 performance regression (Qemu v7.2.4 merger)
- [#2340](https://github.com/xemu-project/xemu/issues/2340) - General performance regression in 0.8.69
- [#2336](https://github.com/xemu-project/xemu/issues/2336) - Xemu 0.8.92 fails to start on Pop!_OS 24.04 LTS
- [#2164](https://github.com/xemu-project/xemu/issues/2164) - Xemu works at half fps/speed instead of full speed
- [#2015](https://github.com/xemu-project/xemu/issues/2015) - vulkan crash on amd r7 240
- [#1890](https://github.com/xemu-project/xemu/issues/1890) - Vulkan renderer crashes when buffers cannot be allocated on startup
- [#1878](https://github.com/xemu-project/xemu/issues/1878) - Windows On ARM - Vulkan renderer on Snapdragon X Series CPUs
- [#1861](https://github.com/xemu-project/xemu/issues/1861) - Vulkan: Crash: Assertion `r->device_props.limits.maxComputeWorkGroupCount[0] >= group_count' failed
- [#1860](https://github.com/xemu-project/xemu/issues/1860) - Vulkan: Shaders are not cached to disk
- [#1854](https://github.com/xemu-project/xemu/issues/1854) - Crashes when choosing Vulkan backend
- [#1844](https://github.com/xemu-project/xemu/issues/1844) - Xemu crashing when Vulkan is select it..
- [#1838](https://github.com/xemu-project/xemu/issues/1838) - Vulkan: Performance Issues
- [#1733](https://github.com/xemu-project/xemu/issues/1733) - Various assertion failures on Windows 11 i5-4590 with Intel(R) HD Graphics 4600 (was "C++ Runtime Popups crashing Xemu")
- [#1694](https://github.com/xemu-project/xemu/issues/1694) - [macOS] [Arm] Xemu crashes when booting from a network volume after v0.7.125
- [#1692](https://github.com/xemu-project/xemu/issues/1692) - Xemu executable won´t start ( does not open ) on my pc using latest builds
- [#1632](https://github.com/xemu-project/xemu/issues/1632) - GPU Crash when installing/using some custom dashboards
- [#1594](https://github.com/xemu-project/xemu/issues/1594) - [Fedora 39] Cannot close Xemu without force quitting it
- [#1581](https://github.com/xemu-project/xemu/issues/1581) - GL error when launching xemu on Intel UHD Graphics 680
- [#1580](https://github.com/xemu-project/xemu/issues/1580) - Emulator crashes when loading game (seemingly due to bad eeprom settings)
- [#1555](https://github.com/xemu-project/xemu/issues/1555) - Silent Crash on startup (Possible Regression)
- [#1441](https://github.com/xemu-project/xemu/issues/1441) - Linux AppImage not loading 2D menus and text in many games
- [#1431](https://github.com/xemu-project/xemu/issues/1431) - xemu crashes when closed before a game launches
- [#1402](https://github.com/xemu-project/xemu/issues/1402) - Severe graphics tearing and wrong/missing textures
- [#1400](https://github.com/xemu-project/xemu/issues/1400) - General use (not ingame) is causing a serious memory leak
- [#1391](https://github.com/xemu-project/xemu/issues/1391) - Exiting Xemu at random points will hang Xemu or slow down the close time.
- [#1385](https://github.com/xemu-project/xemu/issues/1385) - Mac: Command+R (Reset) crashes program every time
- [#1033](https://github.com/xemu-project/xemu/issues/1033) - Sleep function on windows crashes the emulator
- [#967](https://github.com/xemu-project/xemu/issues/967) - [MAC OS]Xemu keeps crashing when loading an iso
- [#806](https://github.com/xemu-project/xemu/issues/806) - loading many games in the same session an cause performance loss over time
- [#687](https://github.com/xemu-project/xemu/issues/687) - Use of `DEBUG_NV2A` can lead to assert in `mttcg_cpu_thread_fn` - race condition?
- [#628](https://github.com/xemu-project/xemu/issues/628) - Miscellaneous Assertions
- [#348](https://github.com/xemu-project/xemu/issues/348) - XEMU may become an unkillable process after assertion.
- [#2112](https://github.com/xemu-project/xemu/issues/2112) - Vulkan: Performance Improvements
- [#2296](https://github.com/xemu-project/xemu/issues/2296) - [Feature Request] Add MoltenVK support
- [#1924](https://github.com/xemu-project/xemu/issues/1924) - Vulkan: Xemu crashes when changing the resolution

## General meta / tracking issues

- [#3095](https://github.com/xemu-project/xemu/issues/3095) - Meta: Subpixel Offset when upscaling
- [#979](https://github.com/xemu-project/xemu/issues/979) - Meta: Texture Sampling Misalignment when upscaling
- [#1099](https://github.com/xemu-project/xemu/issues/1099) - Meta: Timing Issues
- [#1098](https://github.com/xemu-project/xemu/issues/1098) - Meta: Performance Issues
- [#904](https://github.com/xemu-project/xemu/issues/904) - Meta: Audio Issues
- [#1197](https://github.com/xemu-project/xemu/issues/1197) - Meta : FMV Issues
- [#573](https://github.com/xemu-project/xemu/issues/573) - Laggy FMVs
- [#1030](https://github.com/xemu-project/xemu/issues/1030) - WIP Meta : Games which suffer from laggy FMV's

