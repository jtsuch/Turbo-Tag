# Foundry

Built by `Assets/Editor/FoundryBuilder.cs` (`Turbo Tag/Levels/Rebuild Foundry`, job `BuildFoundry`) from Justin's modular kit
(`Assets/Environment/Prefabs`). ~120 x 130 m. Levels follow the kit's 5 m storeys: 0, 5, 10, and a service level at -5.

| Zone | Level | What's there |
|---|---|---|
| Yard | 0 | 48 x 48: a pinwheel of plain 32 x 16 slabs round the Stack's rimmed 16 x 16 dais, a block of tiles over the service level; the Stack (Tower-Round-8m, 10) with a lift; four barriers in a ring round the Stack, a container each side (a stacked pair west); an open trench down into the service level. A lane runs in front of the factory; a rimmed loading bay beside the dock. |
| Service level | -5 | Under the yard's south-east (16 x 16): grated floor, pipe walls, a light ceiling, a container; out by a ramp up the open trench onto the docks. |
| Factory | 0 / 5 / 10 | 9 m walls: arched doors, window bands; a mezzanine at 5 up stairs rising straight onto it; roof at 10 with a skylight hole. |
| Loading dock | 5 | 16 x 16 deck: a long ramp up from the yard (Ramp-16x8, rising north onto its south edge), a ramp on up to the factory roof; containers as the hard way; a low room under it through the factory's east door. |
| Catwalk | 10 | From the factory roof over the dock's front, round a corner piece and south on round pillars; a 3.5 m jump onto the Stack's top. |
| East deck | 5 | A raised deck faced with mixed panels; ramps yard -> 5 -> warehouse roof (10); containers as climbs; the undercroft under it (4 m) through two archways from the yard, flowing into the warehouse hall. |
| Warehouse | 0 / 5 / 10 | An indoor hall 9 m high: a mezzanine (5) along the east wall on beams and pillars, stairs straight onto it, partitions and containers; up top a plain roof (10) reached by the ramp from the deck or the container against its face. Its edge faces are windows, pipes and mixed panels (no flat boundary wall). |
| West void | 0 / 10 | Ring island bridged from the yard, a fan under its hole (rides you up beside the sky deck); a beam under the sky deck to the factory's west door; hex stepping stones south to the docks; the sky deck at 10 flush with the factory roof. |
| West island | 0 / 5 / 10 / 19 | 48 x 64 past the hexes, on its own dirt and propeller: a hall (roof 5) with a room on top (roof 10), up a ramp; a hollow tower (tall walls to 18, a roof at 19 with a hatch) with a staircase of 3 m block steps inside up to the hatch, and a zip line from the roof down to a mini island by the floating plates (bridged to the first plate). Reached by the docks catwalk and a third hex stone. |
| Docks | 0 | Plain quays north and south of one lane (z -48..-40) with two mirrored pits; open to the void south; arch gates; container stacks; the lane runs on west as a catwalk to the west island; a pier catwalk with a teleport pad to the sky deck (and back). |

Kit rules applied (Nov 15; floors and props reworked Nov 11: big plain slabs in the open, one pattern per room, mirrored props sized to the penguin): no Floor-Plate on floors or roofs (ceiling liners only); one straight Floor-Lanes run in front of the factory and one across the docks; mixed walls (Wall-Pipes runs allowed); every ramp/stairs with a start and an end; beams as supports; no flat edge walls (the east boundary wall and its ledge walkway are gone).

Interactables: Fan (ring updraft), zip line (`World/ZipLine`, west tower roof → mini island), TeleportPad pair (`World/TeleportPad`), the Stack lift (Platform-4x4m on `MovingPlatform`).

## The floating island
Everything sits on a floating island: a skirt (flat 16 m climbable faces) hangs just outside the main island's edge, its top a 1 m rim flush with the floor; rock and
dirt below it taper into three hanging lobes, each with a spinning propeller (×3.5 in the middle, ×1.8 under the north
and south lobes). The ring, hexes, sky deck, bridge, plates and pier have small propellers underneath. Kill plane −110.

## Atmosphere
A sunny day (`Editor/Atmosphere.SunnyDay`): custom sky with drifting clouds and a cloud sea below, a warm soft sun,
bright three-colour ambient so interiors read, light haze, the level's own colour grade, and a light wind (streaks and
dust from the west-south-west). Slabs seen from inside carry light ceiling liners.

## Cover props (Nov 11)
Laid out evenly, replacing older ones: a pinwheel of L-covers round the Stack; a run-through container east of it;
a forklift and pallet stacks in the loading bay; open containers facing each other on the docks' south quay, crate
stacks and cable spools mirrored; a generator in the factory; a pipe rack and stepped crates (onto the mezzanine) in
the warehouse hall; two sheds facing each other and a scaffold on the west island.

## Chase lines and cubbies (Nov 12)
Movement: roof hatches over both halls' mezzanines (a drop in, a climb out), a rafter balance beam from a door on the
east deck across the warehouse hall to the mezzanine, the gatehouse on the docks (deck 5, a 6 m jump to the east deck),
a slide gap out of the dock room, a squeeze into the factory from the lane, slide tunnels on the south quay.
Hides (12): crawl platforms (yard, 2), the factory squeeze, wall niches (factory lane face, east deck's dock face, west
hall), rooms under the hollow ramps (yard → undercroft, west hall), the vent run (undercroft → hall), slide tunnels
(docks, 2), a high niche on the tower (a ledge grab up).
