# Stairfall

Built by `Assets/Editor/StairfallBuilder.cs` (`Turbo Tag/Levels/Rebuild Stairfall`, job `BuildStairfall`) from the
modular kit. Nov 13 2026, on the Fracture model. About 176 × 160 m.

**Idea:** a staircase of floating islands. Five islands, each one storey (5 m) from its neighbours: Docks −5, Old Town
0, Works and Terraces +5, Heights +10. More cluttered than Fracture, with structures rather than props: blocks of buildings
with 8 m alleys between them, wall-run panels (WallRunPanel-8m, letter 'R') down the long alley faces, and rooftops as a
second street level.

## Islands
| Island | Height | Extent (x, z) | What's there |
|---|---|---|---|
| Old Town | 0 | −32..32, −32..24 | The Pump Hall and the Chapel (roofs 10), two row houses (roofs 5), the gate house, the north row; a pavilion in the plaza (deck 5) with beams across the alleys to the row houses' roofs; a pit into the Sewers. |
| Sewers | −5 | −24..24, −24..8, under the Old Town | Grated corridors, a hide room. In by the Pump Hall's stairs and the alley pit (bounce pad back up); out south through the cliff onto a pier to the Docks. |
| Docks | −5 | −40..40, −80..−48 | The Fish Market and the Net Shed, container stacks, a container tunnel, two crawl platforms; the zip line from the Tower lands here. |
| Works | +5 | −88..−48, −40..24 | Two halls (roofs 15; a mezzanine in the north one) and a store and shop (roofs 10) either side of a long wall-run alley; yards east and north; a scaffold; a crawl platform. |
| Terraces | +5 | 48..88, −32..24 | The same pattern mirrored: two halls (15, a mezzanine), a store and shop (10), a wall-run alley, a scaffold, a crawl platform. |
| Heights | +10 | −64..64, 40..80 | The North and East halls (roofs 20), two watch houses (15), the hollow Tower (roof 25, inside steps to a hatch, two zip lines out: to the Docks and to the Works), an overpass along the south strip, vent runs. |
| Vaults | +5 | −56..−8, 48..72, under the Heights | Grated corridors, a hide room. In by the North Hall's stairs and a pit (bounce pad back up); out south through the cliff onto a pier to the Works. |

## Routes
- Ramp bridges (one Ramp-16x8m across each 16 m rift, one storey up): Docks → Old Town, Old Town → Works,
  Old Town → Terraces, Works → Heights, Terraces → Heights.
- Tunnels: Sewers → Docks pier, Vaults → Works pier.
- Down fast: zip lines from the Tower (to the Docks, across to the Works), and drops off the higher islands' edges.
- Every building: doors on more than one face, squeezes and slide gaps; the halls have mezzanines or stairs; climb
  faces and scaffolds up onto the roofs.

## Hides (26 in the report)
- Niches, squeezes and slide gaps.
- Crawl platforms, a container tunnel, vent runs.
- The two underground hide rooms and the pits.
- The Tower's roof hatch.
