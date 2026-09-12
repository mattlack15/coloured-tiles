# Tile Colour Game — Prototype Status

Unity 6000.6.0f1 · URP 17.6.0 · AI Navigation 2.0.14 · new Input System (no legacy `Input`)
Project: `/Users/jerrywcy/CSC404/GameJam01` · Scene: `Assets/Scenes/SampleScene.unity`

**Everything is generated at runtime.** No prefabs, no authored assets, no materials on disk.
The scene contains only a `Game` object carrying `GameBootstrap` + `Hud`. The stock Main Camera /
Directional Light / Global Volume are reused and configured from code.

---

## 1. The game, as currently locked in

A floating platform. Most tiles are **dark**; each beat a few small blobs light up in the four
colours. Every agent — you and the crowd — is given a colour, and has that beat to reach a lit tile
of that colour. Bodies are hard, the crowd does not get out of your way, and the edge of the
platform kills.

### Rules

| Rule | Value | Why |
|---|---|---|
| Colours | exactly **4**: red, green, blue, yellow | No tints, shades or highlights anywhere. Extra tones would make it 8 colours to read. |
| Tiles lit per beat | **36 of 196** (18%) — 3 blobs × 3 tiles × 4 colours | Scarcity. Everyone of a colour converges on the same 9 tiles, which is what makes the crowd form. |
| Colour re-roll | **once per beat, for everyone** | An agent's colour is fixed for the whole beat; it never changes mid-journey. |
| Beat length | **10s** | The deadline to cross the platform and claim. |
| Round | **120s**, target **6 claims** | 12 beats → need 6. |
| Claim cooldown | **1.5s** | Bounds claim rate without the dead time a hard one-claim-per-beat cap caused at a 10s beat. |
| Claim requires | standing on a lit tile of your colour, on a *different* tile to your last claim | Stops A→B→A oscillation on adjacent tiles. |

### Deliberate decisions (and what was rejected)

Each of these was a fork that changed the architecture, so they are recorded rather than assumed.

| Decision | Chosen | Rejected alternative |
|---|---|---|
| NPC role | Contested competitors | Ambient set dressing |
| Time model | Real-time continuous | Simultaneous ticks / turn-based |
| Occupation | Hard bodies, cannot push through, a tile can hold several agents | One agent per tile; soft push |
| Tiles | Never consumed — NPCs cannot steal your tile | Claimed tiles disappear |
| Player control | WASD `CharacterController` | Click-to-move / player-as-agent |
| Player avoidance | **NPCs do not avoid the player** | NPCs route around you |
| Platform | No walls, open platform, edge is lethal | Interior walls + safe border |
| Edge hazard | **Shared** — NPCs fall too | Player-only hazard |
| NPC colour lifecycle | Re-rolled per beat | Fixed per journey / permanent |
| NPC targeting | Prefer **far** lit tiles | Prefer nearest |

The two that most shape the feel:

- **NPCs do not avoid the player.** The player is not a `NavMeshAgent`, so Unity's RVO solver is
  blind to it. The crowd simply walks into you. A forward `SphereCast` halts an NPC so it presses
  against you rather than clipping through. Press `G` at runtime to carve the player into the
  navmesh instead and watch how much pressure that removes.
- **NPCs prefer the farthest lit tile.** A crowd that always takes the nearest match never goes
  anywhere. Rewarding distance keeps the whole board in motion, which is what actually puts bodies
  in your way.

---

## 2. Code map

~2,130 lines of C# across 11 files.

| File | Lines | Responsibility |
|---|---|---|
| `Game/GameBootstrap.cs` | 483 | Builds platform / navmesh / player / crowd / camera at runtime. All tuning knobs. Round state. |
| `Agents/NpcBrain.cs` | 424 | One decision per beat, commitment + hysteresis, edge-slip, claim. |
| `Board/BoardManager.cs` | 344 | Tile grid, lit-blob generation, BFS fields, congestion bookkeeping, edge distance. |
| `Agents/NpcLocomotion.cs` | 177 | NavMeshAgent driver, player press-probe, anti-head-on sidestep. |
| `Agents/PlayerController.cs` | 155 | WASD, claim, density slowdown, respawn. |
| `Agents/Faller.cs` | 146 | Shared fall/respawn for player and NPCs. |
| `Core/Palette.cs` | 123 | The four colours, materials, deterministic hash RNG. |
| `Agents/NpcTraits.cs` | 105 | Archetype presets over one scoring function. |
| `Game/Hud.cs` | 92 | IMGUI HUD. |
| `Game/CameraRig.cs` | 42 | Orthographic angled follow camera. |
| `Board/Tile.cs` | 37 | Lit / dark tile visual. |

### Motion stack

| Layer | Runs | Cost |
|---|---|---|
| Planning | per agent, on demand | one 8-directional BFS over 196 cells |
| Decision | per agent, per beat + occasional reconsider | O(lit tiles) ≈ 9 candidates |
| Motion | every frame | steering, RVO, one `SphereCast`, one `OverlapSphere` |

Flow fields were the original plan but are unnecessary at this board size — a per-agent BFS is
cheaper than the plumbing. Revisit if the board grows past roughly 40×40.

### The four archetypes

One scoring function, four presets. Shares are of 80 NPCs.

| Archetype | Share | Behaviour |
|---|---|---|
| Cautious | 32% | Wants the long trip but refuses to be squeezed → takes the clear lane, accidentally showing you one. |
| Anchor | 28% | The exception: takes the *nearest* lit tile, reacts fastest. Rarely crosses the board, claims reliably. |
| Drifter | 22% | Mid-range wanderer (4–8 tiles). Creates traffic without competing. |
| Bold | 18% | Ignores congestion entirely and takes the longest trip. This is the one that body-blocks you. |

---

## 3. Current tuning values

```
board            14 x 14 (196 tiles)     lit/beat 36 (18%)
npcCount         80                      beat 10s   round 120s   target 6 claims   cooldown 1.5s
playerSpeed      4.5                     npcSpeed 3.6-4.7         bodyRadius 0.32
avoidPlayer      false                   npcPhysicsCollisions false   densitySlowdown false
slip             margin 1.05  radius 1.0  crowd 2  outwardDot 0.2  time 0.4s  impulse 2.4
```

Every value is a public field on `GameBootstrap` and tunable live in the Inspector.

> **Unity trap.** Changing a C# field initializer does **not** change an already-serialized value on
> a scene component. The board silently stayed 20×20 / 40 NPCs / 2.0s beat for several runs after the
> defaults were changed in code. Push values explicitly (`EditorUtility.SetDirty` + save scene) or the
> numbers you read in code are not the numbers that run.

---

## 4. Verified behaviour

Measured from live runs, not assumed.

| Behaviour | Result |
|---|---|
| Board generates, navmesh bakes at runtime | ✅ |
| Four colours only, no extra tones | ✅ |
| Colour re-rolls once per beat for player + all NPCs | ✅ |
| Scarcity works — 36 lit tiles, 9 per colour, 0.50 claims/NPC/beat | ✅ |
| Crowd stays in motion — 78/80 moving, avgSpeed 2.79 | ✅ |
| Head-on blocking resolved by sidestep + per-agent aim offset | ✅ |
| Shared edge hazard fires — 53 NPC falls in 17.7s | ✅ (too often — see §5) |
| Player respawn is upright and centred after a fall | ✅ `euler (0,0,0)`, no stray Rigidbody |
| Unwinnable-round guard | ✅ warns when `targetClaims >= beats` |

---

## 5. Known issues / open questions

1. **The crowd physically displaces the player.** After a forced fall the player respawned at
   `(0.5, 0, 0.5)` and ended at `(-4.17, 0.08, -4.07)` with no input pressed. A moving kinematic
   Rigidbody depenetrates a `CharacterController`, so NPC bodies slowly shove you. Consequence: the
   edge is **no longer a purely voluntary hazard**, which contradicts the "crowd cannot shove you
   off" decision. Needs a call: accept it, or make the player immovable by NPCs.
2. **NPC falls are far too frequent.** 53 in 17.7s — the crowd sheds a member roughly every 0.3s,
   which erodes the pressure the crowd is supposed to apply. `slipCrowd = 2` at this density is
   aggressive; `3` would calm it.
3. **`densitySlowdown` is implemented but off and unmeasured.** It was the proposed patch for
   "tiles are never consumed, so nobody can actually deny you anything". Nobody has evaluated
   whether it feels right.
4. **NPC–NPC physics collisions are off** (RVO only). At extreme density agents can visibly overlap.
5. **No tests.** All verification so far has been live runtime probing via the Unity pipeline.
6. **`Physics.OverlapSphereNonAlloc`** is used per-agent per-frame for both the sidestep and the
   density check; fine at 80 agents, would need batching at several hundred.
7. **Runtime-generated everything** — great for iteration, but there is no art pipeline yet.

---

## 6. Roadmap

### Requested, not started

- [ ] **Camera mode switch — three views.** An in-game toggle between:
  1. **Orthographic** angled top-down (the current `CameraRig` behaviour),
  2. **Third person** — perspective, behind/above the player,
  3. **First person** — perspective, at the player's eye height.

  Considerations when picking this up: `GameBootstrap.BuildCamera` and `CameraRig` currently assume
  orthographic; first person needs the player body hidden or head-culled to avoid seeing the inside
  of the capsule; third person needs the crowd to not occlude the player; and the HUD's readability
  of tile colour was designed around a top-down read, so the first-person view may need a colour
  indicator that does not exist yet.

### Next, from the decisions above

- [ ] Resolve open issue 1 (crowd displacement).
- [ ] Retune `slipCrowd` / `slipTime` so falls read as drama rather than attrition.
- [ ] Commit the prototype — currently uncommitted: `SampleScene.unity`, three `ProjectSettings/*`,
      and all of `Assets/Scripts/` (untracked).

---

## 7. Missing entirely

- Audio (no clips, no mixer, no `AudioSource`).
- Front-end: no menu, no pause, no settings, no difficulty select.
- Any UI beyond the throwaway IMGUI HUD (`Hud.cs`).
- Win/lose presentation — the state exists; R restarts.
- Art and animation: everything is Unity primitives. No models, textures, VFX, or character reads.
- Scoring per agent beyond a raw claim count; no leaderboard, no persistent stats.
- Difficulty curve / ramp over a round.
- Mobile or touch input (target platform never confirmed).
- Save / settings persistence.
- Tests and CI.

---

## 8. Operational notes

- **Exit Play Mode before recompiling.** A script edit during Play Mode triggers a domain reload that
  re-runs `Start()` on the existing scene. Private arrays (`_tiles`, `_occupancy`) are not serialized,
  so the reborn `BoardManager` came back hollow while 40 NPCs still pointed at the dead instance —
  2,000 `NullReferenceException`s. `GameBootstrap.CleanupStaleBuild()` plus the `Board.Ready` guard
  now survive it, but exiting first is still the habit.
- **`Faller` has a re-entry trap, now fixed.** It clears `IsFalling` in `FixedUpdate` but delivers the
  teleport from `Update`. Anything that reacted to the still-below-the-platform position inside that
  gap started a phantom second fall. Owners must gate on `Faller.IsAirborne`, never `IsFalling`.
- **`Destroy()` is deferred to end of frame.** A Rigidbody merely *scheduled* for destruction keeps
  simulating and re-applies its tumble over the respawn pose. `Faller` parks the body kinematic
  before anything else.
- **`PlayerSettings.runInBackground = true`** is required or the Editor freezes the player loop when
  unfocused (`Time.time` sat at 0.02 for six seconds, `frame=2`).
- Runtime-created geometry needs `Physics.SyncTransforms()` before `NavMeshSurface.BuildNavMesh()`.
- `NavMeshObstacle`'s field is `carvingTimeToStationary`, not `carvingTimeToSleep`.
- Rigidbody velocity is `linearVelocity` in Unity 6; clearing it on a kinematic body logs a warning.

### Runtime debug keys

`Tab` target lines · `G` toggle NPC-avoids-player · `R` restart
