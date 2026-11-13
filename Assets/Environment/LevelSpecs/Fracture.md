# Fracture

Built by `Assets/Editor/FractureBuilder.cs` (`Turbo Tag/Levels/Rebuild Fracture`, job `BuildFracture`) from
the modular kit. It replaced the greybox Fracture (Nov 12 2026): the same design on the kit standard, with chase lines and hides added.
About 160 × 160 m.

**Idea:** the broken-apart map, floating. Five islands with 16 m of sky between them (14 m rim to rim). Railed bridges
cross the gaps, two optional 6 m jumps cut corners, and pits through the islands have 0.5 m curbs. The ground reads flat
at 0; the height sits in distinct stacks of buildings you can go inside.

## Islands
| Island | Extent (x, z) | What's there |
|---|---|---|
| Centre | −16..16, −16..16 | The Spire. Base 16 × 16 (roof 10): cross doors, a squeeze north, a niche west, a climb face east, inside steps 3 → 6 to a roof hatch. Two round towers on top (20, 30), a scaffold on the roof, the Spire lift to a ledge at 30, and the zip line to the Terrace. L-covers in the corners. |
| South | −80..16, −80..−32 | The Crack (24 × 8, a beam across), the Well (4 × 4), the South hall (roof 5), the Pump house (tunnel tiles, roof 4), two crawl platforms. The Bluff (10): a hall with a mezzanine (5) and a roof hatch, a deck (5) with a room under it, ramp → deck → ramp → roof, climb faces, a hut on top, and a bridge at 10 to the West plateau. |
| East | 32..80, −80..16 | The Sinkhole (16 × 16, a shuttle), the Slot (4 × 4), the Bunker tunnel (on the z 4 line) with a vent run beside it, two container tunnels, a shed. The Terrace (10), the Bluff's mirror: hall, mezzanine, hatch, deck, ramps. The East Block (15) on its roof, whose floor has the hatch down to the mezzanine. |
| West | −80..−32, −16..48 | The plateau (10) over a tiled undercroft (9 m tall) wrapped round a sunken courtyard open to the sky. Switchback ramps A (0 → 5) and B (5 → 10) with landings and a hidden passage under B. Doors, a squeeze, a slide gap and a niche ring the courtyard. A pit through ground and roof. In the dim north hall: a vent run, a crawl platform and a container. The Watch (15), the West hut, an alley on the map edge (container stack), and an east yard where the Centre bridge lands at the z 4 door. A deck at 10 off the north-east corner leads to a long ramp across the sky, down to the North hall's roof (5). |
| North | −16..80, 32..80 | The Twin pits, the North hall (roof 5, a ramp off its north side), two crawl platforms, a container tunnel. The Mesa (10): a hall with a mezzanine, hatch and container. The annex (5) is up a hollow ramp whose room opens into the annex; a ramp from the annex roof reaches the Mesa roof, and another the Mesa top (15). On top: the hollow North tower (25, inside steps to a hatch) and the Crown (35, inside steps to a hatch, a lookout on top). The Sky deck (35) on its lift, a catwalk to the Crown ending in a 4 m hop, and a zip line to the West plateau. |

## Routes
- **Ground loop:** South ↔ Centre ↔ East ↔ North by bridges. West from the Centre (z 4, straight into the plateau's east door).
- **Up high:** the Bluff ↔ West plateau bridge at 10, and the West plateau → North hall roof by the ramp bridge.
- **Cross lines through the Spire:**
  - x −4: South bridge → Spire doors → North jump.
  - z 4: West door → West bridge → Spire doors → East bridge → Bunker tunnel.
- **Risky:** the Centre → North jump (6 m), the South → East jump (6 m), the Crack beam, the Sinkhole shuttle, the hop onto the Crown.
- **Up:** a ramp on every stack; climb faces (Wall-Climb) as the hard way; inside staircases and roof hatches (Spire, North tower, Crown); lifts (Spire 30, Sky 35); zip lines down.

## Hides (36 in the report)
- Niches, squeezes and slide gaps on building faces.
- Crawl platforms (South, North, undercroft).
- Vent runs (Bunker, undercroft).
- Container tunnels.
- Rooms under the hollow ramps and the courtyard landings.
- Roof hatches over the mezzanines.
