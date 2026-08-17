# Aerodrome

A WW1 dogfight game. It remakes the side-view air combat of *The Ancient Art of
War in the Skies* (1992), and nothing else from that game. No strategy map, no
ground war, no bombing runs. You start in the air.

3D aircraft models, locked to one 2D vertical plane. Windows desktop.

## State

Milestones 0 and 1 are done. Milestone 2 is playable.

- `Aerodrome.Core` flies and fights. 99 tests pass in about 20 seconds.
- All three reversals work: the Immelmann, the Split-S, and the flat turn.
  Stall, spin, and spin recovery work.
- Guns, ballistics, jams, and component damage work.
- An energy-aware AI at three skill levels flies through the same controls the
  player uses.
- Two real aircraft: the Sopwith Camel and the Fokker Dr.I, with genuinely
  different strengths. `F8` swaps which one you fly.
- A dive limit. Past the never-exceed speed the airframe takes on stress and
  eventually sheds its wings, so a dive is a decision rather than free speed.
- Up to four opponents and three wingmen, each side flying as a coordinated
  flight rather than a crowd.
- A period instrument board: airspeed, altimeter, tachometer, oil, fuel, bank.
- An aerodrome, a ruined village, a front line and observation balloons, all
  built in code to match the art direction.
- A live flight model panel on `F4`, so the feel can be dialled in while flying.
- Procedural audio with hit markers. `M` mutes, `P` pauses.
- The Godot layer runs at over 900 fps on an RTX 3080 Ti, at about 1 ms a frame.

Still to do for M2: a proper round UI and menus.

## The elevator is not the turn rate

The one modelling idea worth knowing before reading `FlightModel`.

How fast the flight path bends is set by lift and by the G limit. How fast the
NOSE swings is a different and much larger number, because rotating the airframe
is what builds the angle of attack that makes the lift in the first place.

Limiting the nose to the turn rate makes the pilot wait for the flight path
before the aeroplane answers, and it feels like steering a barge. `ElevatorRateRad`
separates them. The nose runs quickly until it reaches the angle of attack the
wing and the airframe allow, and the turn is governed by lift from there.

The envelope is identical either way. Only the time to reach it changes.

## Fighting more than one

Three aircraft all attacking at once is a firing squad, not a dogfight, and it is
not what a flight did anyway. One opponent presses the attack. The rest hold a
perch above and on the far side of you, keeping their height and speed, ready to
take over the moment the attacker loses the position.

Two things follow, and both are the point. You are only ever fought by one
aircraft, so a flight is survivable. And the perch sits between you and the open
sky, so running away from a flight costs something.

Down one of them and the survivors break off for four and a half seconds. That is
the window where you choose what happens next instead of answering somebody
else's choice.

Self-play, twelve rounds a side, a lone Camel against a flight of Dr.Is:

| Opponents | Lone pilot wins | Two or more firing at once |
|---|---|---|
| 1 | 67% | 0.0% |
| 2 | 25% | 0.0% |
| 3 | 17% | 0.0% |

## The three ways to turn around

This is the heart of the game, and the reason it is not just a side-scroller.

| Maneuver | Costs | Keeps |
|---|---|---|
| Immelmann | Speed | Reverses you higher |
| Split-S | Altitude | Reverses you faster |
| Flat turn | 1 s with the guns masked, and a fifth of your speed | Every meter of altitude |

The first two both pay in altitude, which is exactly what you are short of when
somebody has roped you. The flat turn is the answer to that, and it is why all
three have to exist.

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
