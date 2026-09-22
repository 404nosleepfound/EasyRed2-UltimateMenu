# Easy Red 2 - Ultimate Menu

Community-maintained continuation of **Ultimate Menu for Easy Red 2**, originally created by **Avene0**.

## Status

**Active community maintenance** by **404nosleepfound**.

This continuation focuses on keeping the mod compatible with current Easy Red 2 releases, fixing crashes and broken hooks, improving stability, and reviewing useful community feature requests.

## Version 1.4.6

This is the first community-maintained compatibility release.

Highlights:
- Reworked Spawner initialization to avoid the bulk database scans associated with freezes/crashes on newer Easy Red 2 builds.
- Updated the FPS no-recoil hook for the current `FPSGunManager.RecoilEffect` method signature.
- Reduced Harmony reflection warning spam by using direct game types where possible.
- Reworked project references so the source no longer depends on the original developer's local Steam path.
- Targets .NET 6 to match the current BepInEx runtime used by Easy Red 2.
- Added a Windows build helper and repository `.gitignore`.

The safer Spawner scan defaults to vanilla content. Workshop/modded weapon support remains experimental and may be improved in a future release.

## Requirements

- Easy Red 2
- BepInEx 6 IL2CPP

## Installation

1. Install BepInEx 6 IL2CPP for Easy Red 2.
2. Copy `ER2_UltimateMenu.dll` into `Easy Red 2/BepInEx/plugins/`.
3. Start Easy Red 2.
4. Press **Insert** in-game to open Ultimate Menu.

## Building from source

The project reads game references from your Easy Red 2 installation instead of using a hard-coded developer path.

On Windows, run:

```text
BUILD_WINDOWS.bat
```

When prompted, provide your Easy Red 2 installation directory. A successful build outputs `ER2_UltimateMenu.dll` under `build_output/`.

## Bug reports

When reporting a bug, include:
- Easy Red 2 version
- Ultimate Menu version
- BepInEx version
- Reproduction steps
- `BepInEx/LogOutput.log` when possible

## Credits

- **Avene0** — original creator of Ultimate Menu
- **404nosleepfound** — current community maintainer

Thank you to Avene0 for open-sourcing the project and explicitly allowing the community to continue maintaining it.

## License

MIT. The original copyright and permission notice are retained in `LICENSE`.
