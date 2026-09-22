# Changelog

## 1.4.6 - 2026-09-22

First community-maintained compatibility release.

### Fixed
- Reworked Spawner initialization to avoid the bulk `ItemsDatabase.GetAllItemsOfType(...)` calls associated with freezes, crashes, and severe memory growth on newer Easy Red 2 versions.
- Updated the FPS no-recoil patch for the newer four-argument `FPSGunManager.RecoilEffect` signature.
- Removed avoidable Harmony type-resolution paths that produced repeated `UnityEngine.CoreModule` reflection warnings.

### Changed
- Spawner now uses a safer loaded-object scan and logs initialization results to BepInEx.
- Vanilla content is the default safe Spawner mode; modded/Workshop weapon inclusion remains experimental.
- Project targets .NET 6 and uses a configurable Easy Red 2 game directory rather than the original developer's local path.
- Added `BUILD_WINDOWS.bat` for easier local builds.
- Added `.gitignore` for generated build files.

### Validation
- Built successfully against the maintainer's current Easy Red 2/BepInEx environment with 0 warnings and 0 errors.
- In-game testing passed for menu loading, Spawner initialization, weapon spawning, item spawning, vehicle spawning, No Recoil ON, and No Recoil OFF.
- No freeze or crash occurred during the compatibility test.
