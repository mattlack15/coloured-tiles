# Coloured Tiles: Bot AI Design

Version 0.1 · Initial proposal · 12 September 2026

## Purpose

Bots should compete alongside human players by reaching the tile that matches their assigned colour before the round timer expires. They should navigate around other players when practical, push through when necessary, and show recognizable differences in how they make decisions.

This document describes the intended first version. It is a design specification, not a record of implemented features.

Implementation note: the first playable version uses seven bots and integrates the team's three-life rules. Players have 15 seconds to move; black tiles and the edge platform then disappear. After a one-second drop window, wrong-colour occupants launch upward and outward. Survivors remain on their tile, and players with lives remaining respawn on the edge next round. The optional F3 bot debug overlay displays each bot's preset and remaining lives. The original camera, character size, HUD, tile colours, and immediate movement response are preserved. Bot traits remain stable until the game is restarted.

## Shared rules

- Bots learn their assigned colour and destination when the round reveals them. They cannot act on unrevealed information.
- Bots and human players use the same movement speed, acceleration, jumping limits, collision rules, and pushing strength in the first version.
- Every bot can collide with and be pushed by other players, including bots. Avoidance reduces unwanted contact; it does not disable collisions.
- The goal is to reach the assigned tile and remain safely on it when the timer expires.
- Once on the tile, bots make room where possible and reposition to stay safe. Deliberately hunting or knocking players off is outside this first version.

## Round behaviour

### 1. React

When the colour is revealed, the bot identifies its matching tile. It waits for its reaction delay before moving. Small variations in that delay prevent every bot from starting simultaneously.

### 2. Choose a route

The bot weighs travel time, crowding, and exposure to edges or difficult jumps. Personality changes how much each consideration matters.

A reasonable route is one that is physically traversable and has a plausible chance of reaching the tile before the deadline. The bot should estimate the time remaining against its expected travel time, including likely delays from traffic.

Players are movable obstacles. Missing platforms, walls, and jumps beyond the character's abilities are physical restrictions that pushing cannot solve.

### 3. Avoid nearby traffic

While following its route, the bot considers nearby players' positions and movement directions. It can steer around someone, pass behind crossing traffic, or briefly wait for an opening.

After choosing a passing side or a new route, it should commit briefly. It should reconsider sooner if that choice becomes unsafe or blocked, but should not alternate left and right every moment.

### 4. Commit and push

If a detour would take too long, or the bot has stopped making useful progress, it reduces how much room it gives other players and drives along its chosen traversable route. Contact then produces pushing through the shared movement system.

Assertive bots reach this decision earlier. Patient bots allow more time for traffic to clear. All bots become less willing to take lengthy detours when the deadline approaches; this is shared timer awareness, not a separate personality trait.

Pushing does not grant extra strength, speed, immunity, or permission to ignore dangerous gaps.

### 5. Settle and hold

On arrival, the bot chooses an available safe position within the tile instead of always aiming at its exact centre. It slows down and stops trying to advance once safely positioned.

If crowded or displaced, it searches for nearby room on the same tile. It should avoid blocking an entrance when it can move farther inside, while still prioritizing its own survival. It resumes moving toward the tile if pushed off it and recovery remains possible.

## Personality traits

Each bot receives five traits at the start of a match. They remain stable across rounds, with small variations in individual decisions so repeated behaviour does not look scripted.

| Trait | Lower setting | Higher setting |
| --- | --- | --- |
| Assertiveness | Gives others more room and tries avoidance first | Accepts contact and commits to pushing earlier |
| Patience | Quickly tries another response when delayed | Waits briefly for an opening before changing approach |
| Risk tolerance | Prefers wider routes and a margin around edges | Accepts narrow passages and more demanding but feasible jumps |
| Reaction delay | Starts moving soon after the reveal | Takes longer to identify the destination and start moving |
| Commitment | Readily switches to a better route | Sticks with a chosen route despite modest congestion |

Patience concerns how long a bot tolerates a delay. Commitment concerns how readily it abandons its route. Neither should make a bot wait indefinitely or continue into a route that has become impossible.

## Initial personality presets

These presets are starting points for tuning, using the same underlying behaviour.

| Preset | Trait emphasis | Visible behaviour and tradeoff |
| --- | --- | --- |
| Careful planner | Low assertiveness and risk tolerance; moderate patience and commitment | Finds room around crowds, but can lose time on a long detour |
| Bulldozer | High assertiveness and commitment; low patience | Takes direct routes and pushes early, but can waste time against a jam |
| Opportunist | Low commitment; moderate assertiveness and risk tolerance; short reaction delay | Changes course to exploit openings, but can abandon a useful route too soon |

Reaction delay can vary within each preset. The presets should remain distinguishable even when their physical capabilities are identical.

## Recovery and believable mistakes

- Detect a lack of progress over time. Try a sidestep, a short retreat, or another route before repeatedly applying the same unsuccessful movement.
- Use expected travel time to judge progress, so a necessary detour is not mistaken for being stuck merely because it briefly leads away from the target.
- Allow mistakes through delayed reactions, imperfect estimates of traffic, and suboptimal route choices. Do not add arbitrary wrong-colour targeting or random movement failures.
- If no traversable route exists, continue looking for an opening or a feasible alternative without treating empty space as a route. There is no hidden teleport or guaranteed rescue.
- Stop navigation on elimination. Reset the round's target, reaction delay, and temporary movement decisions when a new round begins, while retaining the bot's personality.

## First version checks

The initial implementation should demonstrate that:

1. A bot reaches its assigned tile when given enough time and a clear route.
2. Bots crossing paths attempt to avoid each other and the human player without persistent left-right jitter.
3. A blocked bot takes a reasonable detour when time allows and attempts to push through player congestion when time is short.
4. Contact can displace both bots and humans under the same rules.
5. Several bots sharing a colour use different available positions on their tile and stop advancing once settled.
6. Careful planners, bulldozers, and opportunists show recognizable differences over several rounds.
7. Bots do not move toward their target before the reveal, gain movement advantages, or ignore impossible terrain.

## Tuning during playtesting

Reaction delays, crowd spacing, route commitment time, stuck detection, and pushing thresholds should be tuned against the actual map and round duration.

Tile size and the number of players assigned each colour also need testing together. If everyone assigned a tile cannot physically fit, survival becomes a competition for space. For this first version, assume the tile has enough room for its assigned players, with congestion creating the challenge of getting there.
