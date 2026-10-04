# NepFix

**English** | [Русский](README.ru.md)

**Graphics, FPS and bug fixes for Neptunia Game Maker R:Evolution**

NepFix is an unofficial mod for the Steam PC version of the game. It removes the 60 FPS cap and raises image quality above the game's presets. It also adds NepFX screen-space lighting and fixes engine bugs that show up at high framerates. All settings are changed in-game from the **F10** menu, with no restart needed.

The mod does not touch any game text and contains no translation.

> **Note:** the in-game menu is currently in Russian only. An English menu is planned for an upcoming version.

---

## Features

### Framerate and smoothness
- **FPS cap removal.** You can set your own limit. The default is your monitor's refresh rate.
- **Frame-time based game timing.** The camera and characters no longer shake when FPS drops.
- **Rigidbody interpolation against stutter while running.** Heroines and enemies are excluded so skills and EXE Drives stay intact.
- **Smooth party and bike movement** between the game's physics steps.
- **Character height smoothing.** Removes leg and hair jitter while walking at high FPS.
- **MagicaCloth hair and cloth physics rate:** game default, matched to FPS, or once per frame.
- **GPU frame queue setting:** lower input latency or more even frame pacing.

### Image quality
- **Quality profile:** Unity quality profile up to Ultra.
- **Full-resolution textures.** The game's Normal profile loads them at half size.
- **Texture filtering:** up to 16x anisotropic filtering, plus a sharpness setting for distant textures.
- **4-bone skinning instead of 2,** for smoother joint bending.
- **Anti-aliasing:** up to 8x MSAA, SMAA, and supersampling through render scale up to 2x.
- **AMD FSR 1.0,** with presets from Native AA to Ultra Performance and a sharpness setting.
- **HDR render buffer.**
- **Resolution-aware character outline width.**

### Shadows
- **Shadow quality:** shadow map resolution up to 8192, up to 4 cascades, soft shadows.
- **Minimum shadow distance.** Some maps set it close to zero, and shadows disappear.
- **Shadows for lamps and torches** that the game left without them. Only the nearest lights cast shadows, and you choose how many.
- **SSAO** with strength and radius controls and a full-resolution high-quality mode.

### Lighting
- **Lighting multipliers:** ambient light, environment reflections and how much light characters receive.
- **Brightness limit for colored lamps on characters.** Faces no longer blow out in caves.
- **Cave and indoor detection,** so shadows behave correctly underground.

### NepFX: screen-space lighting
A custom shader injected into the game's URP pipeline.
- **Indirect light:** colored bounce light from nearby surfaces.
- **Ambient occlusion** from ray tracing in corners and crevices.
- **Sun contact shadows:** small shadows where the shadow map can't reach.
- **Overhead light shadows** in caves and mines that have no sun, and a soft floor shadow under characters.
- **Screen-space reflections** on water and wet surfaces, optionally on all floors.
- **Distance and fog fade.** The effect fades out with distance and fog, so distant trees get no dark halos.
- **Separate effect strength for characters,** to keep the anime shading intact.
- **Split-screen comparison:** the left half is without NepFX, the right half is with it.
- **Debug views** for every layer of the effect.

### Draw distance and detail
- **LOD Bias:** detailed models stay visible farther away.
- **Grass and small-object draw distance,** up to no culling at all.
- **Camera far clip distance.**
- **Map simplification off:** the game's simplification of nearby map objects can be disabled.
- **No-freeze scene scans.** Heavy passes over scene objects are spread across frames.

### Game bug fixes
- **Enemies got stuck walking in place at high FPS.** The game decides whether an enemy is blocked by how far it moved in one frame, and that threshold was tuned for 60 FPS. The mod measures real speed over a time window, so the check no longer depends on framerate.
- **Heroines drifted out of frame during EXE Drives and skills.** Physics interpolation on characters caused it, and their bodies are now left alone.
- **Smoother animation transitions** on the field map.
- **Smooth weapon draw:** weapons fade in and out smoothly on out-of-battle attacks.
- **Fewer field voice lines** outside battle. The pause between lines is adjustable.
- **Foot IK** on uneven ground, which can be turned off if feet jitter.

### Bike
- **Bike tuning:** handling, top speed and wall-hit softness.
- **Smooth climbing over ledges.**
- **No passing through walls.** At high speed the physics step gets finer, so the bike no longer clips through thin walls.
- **CSV ride recording** for diagnostics.

### Performance
- **One-click presets:** Max Quality, Balanced, Max FPS.
- **Off-screen skinning disabled.**
- **Faster asset loading.**
- **Bottleneck readout:** the menu shows whether the CPU or the GPU is limiting FPS.

### Menu, monitoring and diagnostics
- **Recommended values:** every setting shows its recommended value next to it.
- **Performance overlay on F11:** FPS, frame time, GPU load and a graph. Four modes, with corner and opacity settings.
- **Freeze logger.** Every noticeable stall is logged with frame time, garbage collection and the mod's own work in that frame. It shows at a glance whether the mod or the game caused it.
- **Automatic MSAA depth-read fix.** The mod picks the right way to read depth to avoid MSAA errors and remembers the choice between launches.
- **Diagnostics to log button** and verbose logging for bug reports.

---

## Requirements
- **The game:** Neptunia Game Maker R:Evolution, Steam version for Windows.
- **Mod loader:** [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) for Windows x64, build be.697 or newer.

## Installation
1. **Install BepInEx.** Download BepInEx 6 IL2CPP for Windows x64 and extract it into the game folder, next to `Neptunia Game Maker REvolution.exe`.
2. **Run the game once** and wait for the main menu. The first launch takes a while because BepInEx generates its interop assemblies. Then close the game.
3. **Install NepFix.** Download the NepFix archive from [Releases](https://github.com/Frenodin/NepFix/releases) and extract it into the game folder. The result should look like this:
   ```
   Neptunia Game Maker REvolution\
   └─ BepInEx\
      └─ plugins\
         └─ NepFix\
            ├─ NepFix.dll
            ├─ nepfx.bundle
            └─ mod.json
   ```
4. **Launch the game** and press **F10**.

Settings are stored in `BepInEx\config\casimoy.nepfix.cfg`. The «Сбросить вкладку» and «Сбросить всё» buttons (Reset tab and Reset all) restore the defaults.

## Uninstall
- **To remove only NepFix,** delete `BepInEx\plugins\NepFix`.
- **To remove BepInEx completely,** delete the following from the game folder:
  - the `BepInEx` folder;
  - `winhttp.dll`;
  - `doorstop_config.ini`;
  - the `dotnet` folder.

The mod does not modify any game files.

## Tips
- **Don't combine** «Шаг физики = 1/FPS» (physics step = 1/FPS) with «Плавное движение» (smooth movement). They do the same job in different ways.
- **If NepFX draws dark spots or halos,** lower «Дальность эффекта» (effect distance) or turn off «Тени сверху и на открытом воздухе» (overhead shadows outdoors).
- **Set an FPS limit** your PC can hold steadily.
- **On NVIDIA GPUs, DLDSR** in the control panel gives an even sharper image. It works on top of DX11 together with the mod.

## Known limitations
- **DirectX 11 only.** The game runs on DirectX 11, and the mod has not been tested on DX12 via `-force-d3d12`.
- **Minimap icon clipping** appears to be a bug in the game itself and is unrelated to the mod.
- **ReShade and DXVK** compatibility has not been tested.

## Reporting bugs
Open an [Issue](https://github.com/Frenodin/NepFix/issues) and attach:
1. `BepInEx\LogOutput.log` right after the problem appears.
2. A screenshot and the place in the game where it happens.
3. Whether the problem persists with NepFX or the relevant option turned off.

## Building from source
**Plugin.** The plugin source is in the `NepFix` folder. Building it requires two things:
- the .NET 6 SDK;
- BepInEx 6 IL2CPP installed in the game. It creates `BepInEx\interop` after the first launch.

Build command:
```
dotnet build NepFix -c Release -p:RefDir="<game folder>\BepInEx"
```
The interop assemblies are generated from game files, so they are not included in the repository.

**NepFX shaders.** The shader project is in `NepFX_Unity`. It requires Unity **2021.3.39f1**, and URP 12 is fetched automatically on first launch. Build command:
```
NepFX_Unity\build_nepfx.bat "<game folder>"
```
The script builds `nepfx.bundle` and copies it into the mod folder.

**Menu illustrations.** They are generated by `tools/previews/render.py`, which renders a small ray-traced scene. The resulting `previews.bin` is embedded into the DLL.

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) and [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop).
- [Harmony](https://github.com/pardeike/Harmony).
- AMD FidelityFX Super Resolution 1.0.

## License
[MIT](LICENSE).

## Disclaimer
NepFix is a fan project and is not affiliated with or endorsed by Idea Factory, Compile Heart or any other rights holders. Neptunia and all related names belong to their respective owners. The mod contains no game files.
