# Aerodrome

A WW1 dogfight game. It remakes the side-view air combat of *The Ancient Art of
War in the Skies* (1992), and nothing else from that game. No strategy map, no
ground war, no bombing runs. You start in the air.

3D aircraft models, locked to one 2D vertical plane. Windows desktop.

## State

Milestone 0 is done. Milestone 1 is in progress.

- `Aerodrome.Core` flies. 42 tests pass.
- The Immelmann, the Split-S, the loop, the stall, and the spin all work.
- The Godot presentation layer does not exist yet.

## Layout

```
src/Aerodrome.Core/         pure C# sim, net9.0, zero engine references
src/Aerodrome.Core.Tests/   xUnit
game/                       Godot project (not started)
assets/source/              raw downloads, never edited, not in git
assets/blender/             .blend work files
assets/export/              .glb files that Godot imports
docs/ASSETS.md              asset license ledger
docs/FEEL.md                flight model tuning log
```

## Why the two layers

`Aerodrome.Core` holds the flight model, ballistics, damage, AI, and match rules.
It has no Godot types in it. It is deterministic: no wall-clock reads, and any
randomness comes from a seeded generator.

That split gives four things:

1. The physics has unit tests.
2. The AI can play thousands of matches headless to check the flight model.
3. Tuning does not need the editor open.
4. Rollback netcode stays possible later.

The Godot layer only renders, plays audio, reads input, and draws the HUD. It
interpolates between sim ticks. No gameplay logic goes in a `_Process` method.

## Build and test

```
dotnet test
```

The sim runs at a fixed 120 Hz. The renderer runs at display rate and
interpolates. Gameplay never reads a frame delta.

## Toolchain

- Godot 4.7.1, Mono/.NET build
- .NET 9 target framework
- Blender 4.5 LTS for model preparation

## Orientation, the one trick worth knowing

Two values describe how an aircraft sits:

- `Theta` is where the nose points, in world radians.
- `CanopySign` is which side of the nose the canopy is on, `+1` or `-1`.

A half loop sweeps `Theta` through 180 degrees and leaves `CanopySign` alone.
The aircraft comes out inverted, and it stays inverted. Only a roll flips
`CanopySign`.

That is why the roll key is manual, and it is why inversion costs something:
a pull runs at the full G limit, but a push is capped near one third of it. An
inverted aircraft has all of its fast turns pointed the wrong way.
