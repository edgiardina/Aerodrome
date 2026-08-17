# Flight model tuning log

One entry per change to how the aircraft flies. Record the reason, not only the
number. A number with no reason gets reverted by accident later.

---

## 2026-08-16: first flying model

Built `Aerodrome.Core` with the full energy model. All 42 tests pass.

### What the model does

Forces each tick: thrust along the nose, drag against the airflow, lift
perpendicular to the airflow, and gravity. Angle of attack is the signed angle
between the nose and the velocity vector, measured toward the canopy.

The pilot commands a heading. The nose slews toward it. The slew rate comes from
the G available:

- Below corner speed the wing cannot make the G. The turn is lift limited.
- Above corner speed the airframe cannot take the G. The turn is structure
  limited, and the radius grows with speed.
- The peak sits at corner speed.

The integrator is semi-implicit Euler at a fixed 120 Hz. It is symplectic, so a
ballistic arc holds its energy. Measured drift over 10 s on a 5.5 km arc is
0.41 m, which is 7e-5 of the total.

### Finding: a real Sopwith Camel cannot do an Immelmann

This is the main result of the day, and it changed the plan.

The honest Camel numbers are all published or derived from published figures.
They give a stall speed of 20.2 m/s and a corner speed of 42.9 m/s, which match
the real aircraft. But the same numbers say a max-G vertical reversal does not
close.

The reason is drag. A WW1 biplane has `Cd0` near 0.040 across 21.5 m2 of wing.
At 4.5 G the induced drag adds about as much again. Total drag in a hard turn
reaches 4500 N against 1370 N of thrust at speed. The aircraft loses energy
about three times faster than the engine can put it back.

Worked example at 55 m/s and 1500 m:

| Quantity | Value |
|---|---|
| Energy height available | 154 m |
| Loop diameter at the G limit | 140 m |
| Energy lost to drag over the arc | 109 m |
| Result | The loop stops about 100 m short |

This matches history. WW1 dogfights were fought in flat turns at low G, not in
the vertical. The Camel's real advantage was a gyroscopic snap turn to the
right, not a loop.

### What we did about it

Two specs now exist.

`AircraftSpec.SopwithCamel` keeps every honest number. It is the reference for
what "correct" means, and the physics tests run against it.

`AircraftSpec.CamelArcade` is what the game ships. It cleans up the airframe and
adds power until the maneuver set works:

| Field | Honest | Arcade | Reason |
|---|---|---|---|
| `Cd0` | 0.040 | 0.018 | Drag was eating the whole energy budget |
| `OswaldEfficiency` | 0.75 | 0.92 | Cuts induced drag in hard turns |
| `EnginePowerW` | 96 940 | 149 000 | 130 hp to about 200 hp |
| `PropEfficiency` | 0.72 | 0.78 | |
| `StaticThrustN` | 2500 | 4200 | Keeps thrust up at low speed at the top of a loop |
| `ClMax` | 1.20 | 1.35 | Lowers stall speed, widens the usable band |
| `GLimit` | 4.5 | 6.0 | Tighter loops, so the loop costs less altitude |
| `TurnRateScale` | 1.0 | 1.15 | Single knob for arcade snappiness |

Resulting envelope, sea level:

| | Honest | Arcade |
|---|---|---|
| Stall speed | 20.2 m/s (73 km/h) | 19.1 m/s (69 km/h) |
| Corner speed | 42.9 m/s (155 km/h) | 46.7 m/s (168 km/h) |
| Peak turn rate | 53 deg/s | 76 deg/s |
| Tightest turn radius | 43 m | 33 m |

### Measured maneuvers, arcade spec

Immelmann from 1500 m at 70 m/s:

| Time | Heading | Altitude | Speed | G |
|---|---|---|---|---|
| 0.0 s | 0 deg | 1500 m | 70.0 m/s | 0.0 |
| 1.5 s | 50 deg | 1533 m | 62.8 m/s | 4.1 |
| 3.0 s | 95 deg | 1607 m | 49.2 m/s | 2.6 |
| 5.5 s | 174 deg | 1684 m | 36.1 m/s | 1.2 |

Then the roll rights it. Net: reversed, 185 m higher, 34 m/s slower, upright.

The loop is egg shaped, not round. As the aircraft slows, the rate for a given G
goes up, so the radius shrinks toward the top. That is correct, and it is what
makes the maneuver close at all.

Split-S from 1500 m at 45 m/s: reversed, 217 m lower, 76 m/s. Upright with no
second roll, because the pull started inverted.

Energy height stayed near flat across the Immelmann, 1750 m to 1750 m. Thrust
and drag almost cancel at this pull rate. That is a good sign: the maneuver is
neither free nor punishing.

### Finding: inversion starts at the midpoint of the roll

The canopy crosses the horizon halfway through the 0.35 s roll, so the fuel
starvation clock starts 0.175 s before the roll finishes. A pilot who starts a
roll and changes their mind has already paid part of the cost.

This came out of a failing test, and the model was right. There is now a test
that pins the behavior, because it is a design property and not an accident.

---

## 2026-08-16 (later): the flat turn

Ed pointed out a missing maneuver, and he was right. The design had only two ways
to reverse, and both of them are vertical:

- Immelmann: trade speed for height.
- Split-S: trade height for speed.

The original had a third. Press the direction you are not facing and the aircraft
swaps ends through the screen depth, keeping its altitude. Without it, a pilot who
gets roped has no answer that does not cost altitude, and altitude is the thing
they are short of. It is a real hole in the strategy.

### What it costs

The maneuver has to cost something or it beats the other two every time. Three
costs, and Ed named all three:

1. **Time.** 0.95 s, and it cannot be interrupted. Heading, roll, and a second
   press are all ignored until it finishes.
2. **The guns.** Halfway round, the nose points straight into or out of the
   screen. Nothing can be shot at. This is the whole vulnerability window, and
   it is what a good opponent aims at.
3. **Speed.** 22 percent of airspeed. That is what you pay instead of altitude.

There is a fourth, and it fell out of the model rather than being designed: you
cannot flat turn below stall speed. Too slow means you must dive for speed first,
which costs the altitude you were trying to protect.

### How it is modelled

The aircraft yaws 180 degrees about the world Y axis. On commit:

- `Theta` mirrors about the vertical: `theta -> PI - theta`. A 20 degree climb to
  the right becomes a 20 degree climb to the left.
- `CanopySign` flips, so the aircraft leaves the turn the same way up it went in.

Both changes together keep the rendered transform continuous, so there is no
visual pop on the commit tick. The renderer snaps rather than interpolates across
that one tick, because interpolating a yaw reset against a heading mirror would
spin the model.

The in-plane X velocity follows `cos(p * PI)`, which is the true projection of a
constant-speed 180 onto our plane. It passes through zero halfway. The aircraft
appears to hang for a moment, which is exactly right.

### Honesty note

A real flat 180 at 60 m/s needs about 200 m of radius and takes 11 seconds. This
takes one. The geometry hides in the Z axis where the player cannot see it, so
unlike the arcade drag numbers this cheat is invisible. It plays the way the
original played, which is the point.

### Making it look flown, not scripted

The first version yawed about the world vertical and nothing else. It read as a
model on a turntable, because rudder alone is not how an aircraft turns. Three
changes fixed it:

1. **Bank.** A bell curve peaking at 65 degrees, level at both ends. A level 180
   is a banked turn.
2. **The bank leads the yaw.** Bank runs off raw progress, yaw runs off a
   smoothstep of it. So the aircraft rolls first and comes round because it is
   banked. A quarter of the way in the bank is already past 70 percent while the
   yaw is barely 10 percent. There is a test that pins the ordering.
3. **The yaw eases.** Smoothstep, so it rolls in, whips through the middle, and
   rolls out, rather than pivoting at a constant rate. The velocity projection
   tracks the same eased curve, so the flight path and the model agree.

Plus real control surfaces on the model: ailerons, elevator, and rudder on their
own hinges. The ailerons reverse at the halfway point, which is the moment the
pilot stops rolling in and starts rolling out. That reversal is small and it is
most of what sells the maneuver.

Surfaces are driven everywhere, not only in the flat turn. Elevator tracks angle
of attack, so you can see the aircraft pulling. Ailerons go hard over during a
roll.

### Bug this found

The first version reported in-plane speed as airspeed. Mid-turn the HUD read
30 km/h and every stall warning lit up, on an aircraft doing 48 m/s. Airspeed now
reports true speed through space. There is a test that pins it.

---

### Open items for the next pass

1. Tune against the original in DOSBox. Nothing here has been compared to
   AAOWITS yet. `TurnRateScale` is the first knob to reach for.
2. `WeathercockGain` of 3.0 is a guess. It sets the trim angle a sustained pull
   settles at, which came out near 7 degrees. That looks right, but it is not
   measured against anything.
3. The arcade top speed is about 72 m/s (260 km/h). A real Camel did 51 m/s
   (185 km/h). Decide whether that matters for the feel.
4. Decide whether other aircraft types scale from the arcade preset or get their
   own honest-then-tuned pass.

---

## The flight model panel

`F4` puts every number above on a slider, live, while you fly.

The reason is that none of the open items below can be settled by argument. The
only way to know whether a loop should take 3.0 s or 3.4 s is to fly both, ten
seconds apart. Editing a constant, rebuilding and relaunching breaks that loop
badly enough that you stop trying things, which is how a tuning pass turns into
a set of numbers nobody has questioned since the day they were written.

The panel writes real `AircraftSpec` fields, so a setting that feels right can go
straight into the source with no translation.

Two things it shows that a source file does not:

- **Stall speed, corner speed, peak turn rate, and loop time**, recomputed as you
  drag. Corner speed is the number the whole dogfight orbits around, and it is a
  derived value, so a change to mass or to `ClMax` moves it in ways that are hard
  to predict by reading.
- **A tick at the shipped value on every bar.** You can always see how far you
  have wandered, and get back by eye.

Enemy aircraft take the same change as a **ratio** of their own baseline. Setting
`ClMax` to 2.2 on the Camel does not set the Dr.I to 2.2, it raises the Dr.I by
the same proportion. The two aircraft are meant to be different, and a panel that
flattened them would quietly undo the only reason there is more than one.

### What to reach for first

1. `nose slew cap` and `turn rate scale` for how sharp the aircraft feels.
2. `parasite drag Cd0` and `engine power` for how fast it bleeds and rebuilds
   energy. These decide whether extending away is a real option.
3. `camera view width` is on the panel too. It is not a spec field, but how close
   the camera sits changes the game as much as any of them: it decides how much
   warning you get.

### Still open

The items below are unchanged. The panel does not answer any of them, it only
makes them cheap to answer.

1. Tune against the original in DOSBox. Nothing here has been compared to
   AAOWITS yet.
2. `WeathercockGain` of 2.0 is still a guess.
3. The arcade top speed is still about 260 km/h against a real 185 km/h.

---

## The elevator, and why pitch felt like a barge

Ed: "the left right feels good but the elevator pitching is too realistic".

He was right, and the cause was a modelling mistake rather than a number that
needed turning up.

`MaxSlewRate` returns the rate at which the FLIGHT PATH can bend. It comes
straight out of the lift the wing can make and the G the airframe can take, and
it is correct for what it is. The mistake was using it to rate-limit the NOSE.

Those are different things. Rotating the nose is what BUILDS the angle of attack
that makes the lift that bends the flight path. It happens far faster than the
turn, and it has to, because it comes first. A pilot pulls the stick, the nose
comes up almost at once, and the aeroplane arcs round after it.

Limiting the nose to the turn rate inverted that. The pilot had to wait for the
flight path before the nose would answer, so a hard pull took most of a second to
arrive, which is exactly the heavy unwilling elevator Ed was describing.

### The fix

`ElevatorRateRad` is how fast the elevator can rotate the airframe about its own
centre of gravity. The Camel gets 5.5 rad/s, the Dr.I 5.8 because it was famously
twitchy in pitch. The nose runs at that rate until it hits the angle of attack
limit, and then the turn is governed by lift exactly as before.

The angle of attack limit is the smaller of two things:

- **The wing.** Past the stall angle there is no more lift to be had.
- **The airframe.** At speed the structural G limit binds well before the stall
  angle does. This one is not optional: without it the elevator would out-turn
  the G limit and corner speed would stop meaning anything, and corner speed is
  the number the whole dogfight orbits around.

**The envelope is unchanged.** Same stall speed, same corner speed, same peak
turn rate, same loop time. Only the time taken to get there is different. That
matters, because the sustained turn is the part Ed said already felt good.

Both numbers are on the F4 panel.

### What it broke, and what that turned out to mean

The skill ladder inverted immediately: the Ace went from beating everyone to
losing to a Rookie two rounds in three.

Two guesses were wrong before the measurement was right. It was not oscillation
(the Ace flew the SMOOTHEST of the three) and it was not the pursuit command
easing off on arrival (changing that moved nothing). Measuring how each skill
actually flew, rather than whether it won, was what found it: the Ace flew fast
at low angle of attack, and got shot to pieces at close range.

The Ace was flying PURE pursuit. It tracked the target's current position
accurately, arrived, and flew a gentle arc into the other aeroplane's guns. The
Rookie's 0.55 s of stale data, dead-reckoned forward, threw its aim point wide of
a turning target, and wide is roughly where lead pursuit wants you. The worst
data was flying the better geometry.

That confirmed a hypothesis written down in `SelfPlayTests` a while ago and never
tested. Making the lead deliberate instead of accidental, by estimating the
target's turn rate and flying at a point ahead on its arc, is the largest single
change ever measured on this ladder:

| | before | after |
|---|---|---|
| Ace over Rookie | 23% | 58% |
| Ace over Veteran | 37% | 57% |

A Veteran still does not beat a Rookie, which is the residue of the same effect
and is recorded in the test. The lesson worth keeping: measure what the AI is
DOING, not whether it is winning. Win rate says a pilot is worse. Angle of
attack, airspeed and cause of death say why.

---

## The camera bounce

Ed: "the camera seems to bounce constantly to follow the plane instead of
smoothly locked on".

Three causes, and the trace that found them is now permanent. Run a capture and
the game prints the camera's own speed statistics every two seconds. The number
to watch is the SPREAD, which is the standard deviation of camera speed over its
mean. A camera locked on to a steadily flying aircraft moves at a nearly constant
rate, so the spread is small. One that lurches and stalls swings between zero and
fast, and the spread shows it at once.

### 1. The deadzone measured from the wrong thing

```
if (desired.DistanceTo(_targetCenter) > DeadzoneM) _targetCenter = desired;
```

It compared the aircraft against the last TARGET rather than against the camera,
and on exceeding the threshold it snapped the target onto the aircraft. That is a
loop:

1. The target jumps nine metres onto the aircraft.
2. The aircraft is now inside the deadzone of the NEW target, so the target
   freezes.
3. The camera eases in and stops.
4. The aircraft drifts another nine metres and it jumps again.

At 240 km/h that is about eight lurches a second.

The trace made it unmistakable. The old camera reported a worst jerk of about
**41,300 m/s² in nearly every two second window**, and it was the same figure
every time. Hard flying does not produce a constant number. A fixed nine metre
snap does.

Now the slack is measured from the camera, and once outside it the target tracks
continuously with the deadzone held as a trailing radius.

### 2. The duel framing had no hysteresis

One metre outside the framing range the camera wanted 250 m of width. One metre
inside it wanted 520. A dogfight sits on that boundary and crosses it several
times a second, so the view pumped in and out by a factor of two continuously.

Both the centre and the width now cross-fade over a 130 m band.

### 3. The lead vector hung off the nose

The solo lead used `cos(Theta) * Airspeed`, which is speed along the NOSE rather
than the velocity vector. Those diverge under any real pull, and they diverged a
great deal more the day the elevator got quick: the nose can now swing most of a
right angle in a tenth of a second, and a sixty metre lead vector hanging off it
threw the camera across the arena every time the pilot twitched.

The camera now reads the true velocity, which is smooth by construction.
`RenderState` carries it.

### Measured

Steady flight, ignoring the deliberate Far View transitions:

| | before | after |
|---|---|---|
| speed spread | 0.37 to 1.08 | 0.02 to 0.13 |
| worst jerk | ~41,300 m/s² every window | 44 to 4,900 m/s² |
