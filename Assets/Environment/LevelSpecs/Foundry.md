# Foundry

Built by `Assets/Editor/FoundryBuilder.cs` (`Turbo Tag/Levels/Rebuild Foundry`, job `BuildFoundry`) from Justin's modular kit
(`Assets/Environment/Prefabs`). ~120 x 130 m. Levels follow the kit's 5 m storeys: 0, 5, 10, and a service level at -10.

| Zone | Level | What's there |
|---|---|---|
| Yard | 0 | 48 x 48 of plate/tile/grate/lane tiles; the Stack (Tower-Round-8m, 10) with a lift; containers (a stacked pair), barriers, half walls, pillars; two Floor-Hole tiles drop into the service level; an open ramp trench down into it. |
| Service level | -10 | Under the yard's south-east: grated floor, pipe/vented walls, a hazard partition, containers; out by two ramps up the trench. |
| Factory | 0 / 5 / 10 | 9 m walls: arched doors, window bands; a mezzanine at 5 up stairs rising straight onto it; roof at 10 with a skylight hole. |
| Loading dock | 5 | 16 x 16 deck: a long ramp up from the yard (Ramp-16x8, rising north onto its south edge), a ramp on up to the factory roof; containers as the hard way; a low room under it through the factory's east door. |
| Catwalk | 8 | From the factory roof over the dock's front, south on round pillars to a landing by the Stack (its top: one 4 m climb). |
| East deck | 5 | A raised deck faced with mixed panels; ramps yard -> 5 -> warehouse roof (10); containers as climbs; the undercroft under it (4 m) through two archways from the yard, flowing into the warehouse hall. |
| Warehouse | 0 / 5 / 10 | An indoor hall 9 m high: a mezzanine (5) along the east wall on beams and pillars, stairs straight onto it, partitions and containers; a container on the mezzanine under a skylight is the hard way onto the roof (10). Its edge faces are windows, pipes and mixed panels (no flat boundary wall). |
| West void | 0 / 10 | Ring island bridged from the yard, a fan under its hole (rides you up beside the sky deck); a beam under the sky deck to the factory's west door; hex stepping stones south to the docks; the sky deck at 10 flush with the factory roof. |
| Docks | 0 | Open to the void south; three tiles missing (pits); arch gates; container stacks; a boost pad east down the lane over a pit; a pier catwalk with a teleport pad to the sky deck (and back). |

Kit rules applied (Nov 15): no Floor-Plate on floors or roofs; Tiles/Inlay mixed outdoors, Tiles mostly indoors with rare Grate; one straight Floor-Lanes run in the yard and one on the docks; mixed walls (Wall-Pipes runs allowed); every ramp/stairs with a start and an end; beams as supports; no flat edge walls (the east boundary wall and its ledge walkway are gone).

Interactables: Fan (ring updraft), BoostPad (`World/BoostPad`), TeleportPad pair (`World/TeleportPad`), the Stack lift (Platform-4x4m on `MovingPlatform`).

## The floating island
Everything sits on a floating island: a skirt (flat 16 m climbable faces) hangs under the main island's edge, rock and
dirt below it taper into three hanging lobes, each with a spinning propeller (×3.5 in the middle, ×1.8 under the north
and south lobes). The ring, hexes, sky deck, bridge, plates and pier have small propellers underneath. Kill plane −110.
