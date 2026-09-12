# Coloured Tiles

Unity 6000.3.24f1 · 3D map prototype · no external assets

## Open

In Unity Hub, choose **Add project from disk** and select this repository folder. Open with Unity 6000.3.24f1. Open `Assets/Scenes/FloatingTiles.unity` and press Play. The generated scene and materials are included.

If needed, use **Floating Tiles → Create Map Scene** to regenerate the scene. Save any other scene first: this command opens a new scene.

## Round

- 0–10 seconds: all 25 floating tiles are black with pale blue outlines. The surrounding ring is solid.
- At 10 seconds: four distinct random tiles turn blue, red, yellow and green. Other tiles remain black.
- At 15 seconds: the ring colliders switch off immediately. Its visual surface fades over one second, then is disabled.
- All 25 tiles stay solid throughout. No tile removal or subsequent rounds are assumed.
- A red kill plane below the map kills the test player. A fallback below the plane also catches falls outside it.

Select **Floating Map** in the Hierarchy to adjust initial seconds, reveal seconds, fade duration or lit tile count (3–4). The default uses all four colours. With three tiles, blue, red and yellow are used.

## Test player

WASD / arrow keys move in world directions. Space jumps. R reloads the scene and restarts the timer, including after death. The player starts on the south platform. A fixed camera shows the whole map.

`TestPlayer` is only a movement/death harness. `KillPlane` currently targets that component; adapt its `Die()` call when integrating your own player. `FloatingMap.spawnPoint` identifies the intended spawn location.

## Verification

All C# scripts compiled successfully against the installed Unity 2023.1 libraries; Unity 6 has not been tested locally. An automated Unity import/play test was attempted but could not run because the background editor could not connect to the local Unity licensing service. First-import scene generation, shader rendering and gameplay still require verification in the licensed Unity editor.
