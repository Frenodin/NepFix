# NepFix

**English** | [Русский](README.ru.md)

**Graphics, FPS and bug fixes for Neptunia Game Maker R:Evolution**

NepFix is an unofficial mod for the Steam PC version of the game. It removes the 60 FPS cap and raises image quality above the game's presets. It also adds NepFX screen-space lighting and fixes engine bugs that show up at high framerates. All settings are changed in-game from the **F10** menu, with no restart needed. The menu is available in **English and Russian**.

The mod does not touch any game text and contains no translation.

---

## Screenshots
![NepFix menu](docs/screenshots/showcase.png)

<details>
<summary><b>All menu tabs</b></summary>

<table>
<tr><td align="center"><a href="docs/screenshots/01-display.png"><img src="docs/screenshots/01-display.png" width="260" alt="Display"><br><sub>Display</sub></a></td><td align="center"><a href="docs/screenshots/02-quality.png"><img src="docs/screenshots/02-quality.png" width="260" alt="Quality"><br><sub>Quality</sub></a></td><td align="center"><a href="docs/screenshots/03-anti-aliasing.png"><img src="docs/screenshots/03-anti-aliasing.png" width="260" alt="Anti-aliasing"><br><sub>Anti-aliasing</sub></a></td></tr>
<tr><td align="center"><a href="docs/screenshots/04-shadows.png"><img src="docs/screenshots/04-shadows.png" width="260" alt="Shadows"><br><sub>Shadows</sub></a></td><td align="center"><a href="docs/screenshots/05-distance.png"><img src="docs/screenshots/05-distance.png" width="260" alt="Distance"><br><sub>Distance</sub></a></td><td align="center"><a href="docs/screenshots/06-characters.png"><img src="docs/screenshots/06-characters.png" width="260" alt="Characters"><br><sub>Characters</sub></a></td></tr>
<tr><td align="center"><a href="docs/screenshots/07-characters-bike.png"><img src="docs/screenshots/07-characters-bike.png" width="260" alt="Bike and voice"><br><sub>Bike and voice</sub></a></td><td align="center"><a href="docs/screenshots/08-fixes.png"><img src="docs/screenshots/08-fixes.png" width="260" alt="Fixes"><br><sub>Fixes</sub></a></td><td align="center"><a href="docs/screenshots/09-monitoring.png"><img src="docs/screenshots/09-monitoring.png" width="260" alt="Monitoring"><br><sub>Monitoring</sub></a></td></tr>
<tr><td align="center"><a href="docs/screenshots/10-overlay.png"><img src="docs/screenshots/10-overlay.png" width="260" alt="Overlay"><br><sub>Overlay</sub></a></td><td align="center"><a href="docs/screenshots/11-performance.png"><img src="docs/screenshots/11-performance.png" width="260" alt="Performance"><br><sub>Performance</sub></a></td><td align="center"><a href="docs/screenshots/12-lighting.png"><img src="docs/screenshots/12-lighting.png" width="260" alt="Lighting"><br><sub>Lighting</sub></a></td></tr>
<tr><td align="center"><a href="docs/screenshots/13-lighting-nepfx.png"><img src="docs/screenshots/13-lighting-nepfx.png" width="260" alt="NepFX"><br><sub>NepFX</sub></a></td><td align="center"><a href="docs/screenshots/14-lighting-ssr.png"><img src="docs/screenshots/14-lighting-ssr.png" width="260" alt="Reflections and shadows"><br><sub>Reflections and shadows</sub></a></td><td align="center"><a href="docs/screenshots/15-lighting-quality.png"><img src="docs/screenshots/15-lighting-quality.png" width="260" alt="Effect quality"><br><sub>Effect quality</sub></a></td></tr>
</table>

</details>

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
- **English and Russian menu.** The language follows your system by default and can be switched with the button in the menu's title bar or on the Display tab.
- **Recommended values:** every setting shows its recommended value next to it.
- **Performance overlay on F11:** FPS, frame time, GPU load and a graph. Four modes, with corner and opacity settings.
- **Freeze logger.** Every noticeable stall is logged with frame time, garbage collection and the mod's own work in that frame. It shows at a glance whether the mod or the game caused it.
- **Automatic MSAA depth-read fix.** The mod picks the right way to read depth to avoid MSAA errors and remembers the choice between launches.
- **Diagnostics to log button** and verbose logging for bug reports.

---

## Installation
Everything you need is in a single archive. There is no need to download BepInEx separately.

1. Download **`NepFix-x.x.x-with-BepInEx.zip`** from [Releases](https://github.com/Frenodin/NepFix/releases/latest).
2. Open the game folder. In Steam, right-click the game → **Manage** → **Browse local files**.
3. Extract the archive right there, next to `Neptunia Game Maker REvolution.exe`. Allow Windows to replace files if asked.
4. Launch the game through Steam. A black BepInEx console window will appear, which is expected. **The first launch takes 1–5 minutes** while BepInEx generates its files from the game code. Do not close the game, even if it looks frozen.
5. In the main menu press **F10** to open the NepFix menu. **F11** toggles the FPS overlay.

Later launches are fast. A good starting point is the **Balanced, recommended** preset on the **Performance** tab.

After extracting, the game folder should look like this:
```
Neptunia Game Maker REvolution\
├─ BepInEx\
│  └─ plugins\
│     └─ NepFix\
├─ dotnet\
├─ doorstop_config.ini
├─ winhttp.dll
└─ Neptunia Game Maker REvolution.exe
```

### Updating
If BepInEx is already installed, the smaller **`NepFix-x.x.x.zip`** without BepInEx is enough. Extract it into the game folder and replace files.

### Troubleshooting
| Problem | Fix |
|---|---|
| No console window, F10 does nothing | `winhttp.dll` must be next to the exe. Your antivirus may have removed it: add the game folder to exclusions and extract again. |
| Game crashes on first launch | Delete `BepInEx\interop` and `BepInEx\cache`, then launch again. |
| Steam Deck / Linux | Steam launch options: `WINEDLLOVERRIDES="winhttp=n,b" %command%` |

Settings are stored in `BepInEx\config\casimoy.nepfix.cfg`. The **Reset tab** and **Reset all** buttons restore the defaults.

## Uninstall
- **Mod only:** delete `BepInEx\plugins\NepFix`.
- **Everything:** delete the `BepInEx` and `dotnet` folders plus `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and `NepFix_README.txt` from the game folder.

The mod does not modify any game files.

## Tips
- **Don't combine** "Physics step matched to FPS" with "Smooth party and bike movement". They do the same job in different ways.
- **If NepFX draws dark spots or halos,** lower "Effect distance" or turn off "Overhead shadows outdoors without sun".
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

## Support
NepFix is and will stay free. If you'd like to support my future projects, you can do it on [Patreon](https://www.patreon.com/Frenodin).

[![Support on Patreon](https://img.shields.io/badge/Patreon-Support-F96854?logo=patreon&logoColor=white)](https://www.patreon.com/Frenodin)

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx), bundled in the release archive under LGPL-2.1, and [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop).
- [Harmony](https://github.com/pardeike/Harmony).
- AMD FidelityFX Super Resolution 1.0.

## License
[MIT](LICENSE).

## Disclaimer
NepFix is a fan project and is not affiliated with or endorsed by Idea Factory, Compile Heart or any other rights holders. Neptunia and all related names belong to their respective owners. The mod contains no game files.
