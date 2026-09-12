# Coloured Tiles

Open this project in Unity **6000.3.24f1**, then open `Assets/Scenes/FloatingTiles.unity` and press Play.

## Round flow

1. The test player starts on the edge platform. All 25 tiles are black for 10 seconds at game start.
2. Four random tiles light up blue, red, yellow and green. The HUD assigns the player one of these colours.
3. The player has 15 seconds to reach that colour. The edge platform is available during this time.
4. Black tiles and their outlines disappear, and the edge platform becomes unavailable.
5. After a one-second drop window, players above a wrong-colour tile launch outward. Jumping does not avoid the colour check.
6. After four seconds for falls to resolve, black tiles return and coloured tiles fade to black over one second.
7. The next round reveals new colours. The edge platform returns; dead players respawn there, while survivors stay on their tile.

The initial 10-second black phase happens only once. The player loses one life per fall and waits for the next round; there is no game-over limit yet. No multiplayer or player pushing is implemented.

## Controls and settings

WASD / arrows move; Space jumps; R restarts the entire game. The HUD shows the target colour, round, timer and lives lost.

Select **Floating Map** in the Hierarchy to adjust `Initial Seconds`, `Move Seconds`, `Drop Seconds`, `Resolve Seconds`, `Fade Seconds` and `Lit Tile Count` (3–4). Three tiles use blue/red/yellow. Targets are always selected from revealed colours. The map finds the single Test Player automatically if its Player field is empty.

Expand **Floating Map** for tiles, outlines, platforms and Spawn Point. Kill Plane, Test Player and Main Camera are separate root objects. The edge disappears at each movement deadline to prevent using the respawn area to avoid the round.

The Test Player exposes `Die`, `Respawn` and `LaunchOff` for this map prototype. Replace this harness when integrating a full player controller. `Floating Tiles → Create Map Scene` rebuilds the scene; save other work before using it.

## Validation

C# compilation against installed Unity 2023.1 libraries passes. The repository remains configured for Unity 6; Unity 6-specific editor validation is still pending.

An isolated automated Play-mode check was attempted, but Unity could not connect to its IL post-processing service. Round transitions still need a Play-mode check in the editor.
