using System;
using System.Collections.Generic;

namespace Aerodrome.Core;

public enum FlightRole
{
    /// <summary>Presses the attack. Exactly one living member of a flight holds this.</summary>
    Engaged,

    /// <summary>Holds a perch, keeps its energy, and waits for the fight to come to it.</summary>
    Supporting,
}

/// <summary>What the flight has told one pilot to do this tick.</summary>
public readonly record struct FlightOrders(FlightRole Role, Vec2 Station)
{
    /// <summary>A pilot with nobody to coordinate with presses the attack itself.</summary>
    public static FlightOrders Solo => default;
}

/// <summary>
/// Several aircraft on one side, fighting as a unit rather than as a crowd.
///
/// Three opponents that all fly straight at you is not three times the fight, it
/// is a firing squad. Everyone arrives together, everyone shoots at once, and the
/// round is over before it starts. Worse, it is not even how it worked: a WW1
/// flight put one aircraft in and kept the rest high, because two fighters in the
/// same piece of sky mostly get in each other's way.
///
/// So exactly one member is Engaged at a time. The rest hold a perch above and on
/// the far side of the target, keeping height and speed. They still shoot if you
/// fly across their nose, and they take the fight over the moment the engaged
/// pilot loses the position.
///
/// Two things fall out of that, and both are the point. You can only ever be
/// fought by one aircraft, so a flight is survivable. And the perch sits between
/// the target and the open sky, so running away from a flight costs something,
/// which is the disengage problem the second aircraft type only half solved.
///
/// Pure Core. Deterministic, no allocation per tick, and it decides nothing the
/// pilots cannot decide for themselves. It only says who goes in.
/// </summary>
public sealed class Flight
{
    /// <summary>How far above the target a supporting pilot waits.</summary>
    public const double PerchHeightM = 200.0;

    /// <summary>How far to the far side of the target the perch sits.</summary>
    public const double PerchOffsetM = 320.0;

    /// <summary>
    /// Shortest time between handovers. Without it the flight swaps whoever is
    /// momentarily nearest every few ticks, nobody ever finishes an attack, and it
    /// plays worse than having no coordination at all.
    /// </summary>
    public const double HandoverHoldS = 3.0;

    /// <summary>How long the engaged pilot may be out of position before relief.</summary>
    public const double OutOfPositionS = 1.6;

    /// <summary>
    /// How long a flight breaks off after losing one of its own.
    ///
    /// This is the single number that makes fighting several aircraft possible
    /// rather than merely tidy. Without it the flight is a relay: the survivors
    /// hand the attack straight on, the lone pilot never gets a breath, and
    /// self-play had it winning none of twelve rounds against three. With it,
    /// downing one buys you the seconds to climb, reload the situation, or leave.
    ///
    /// It is also what anybody would actually do on watching a friend go down.
    /// </summary>
    public const double ShakenSeconds = 4.5;

    public int Team { get; }

    /// <summary>The member currently pressing the attack, or null if nobody can.</summary>
    public Combatant? Engaged { get; private set; }

    /// <summary>Who the flight as a whole is fighting.</summary>
    public Combatant? Target { get; private set; }

    private readonly List<Combatant> _members = new();
    private readonly List<FlightOrders> _orders = new();

    private double _sinceHandover = HandoverHoldS;
    private double _outOfPosition;
    private double _shakenFor;
    private int _lastAlive = -1;

    public Flight(int team) => Team = team;

    /// <summary>True while the flight has broken off after losing one of its own.</summary>
    public bool IsShaken => _shakenFor > 0;

    public IReadOnlyList<Combatant> Members => _members;

    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var m in _members) if (m.IsAlive) n++;
            return n;
        }
    }

    public void Add(Combatant member)
    {
        _members.Add(member);
        _orders.Add(FlightOrders.Solo);
    }

    /// <summary>
    /// What this pilot has been told to do. A member of no flight, or a flight with
    /// nothing to fight, gets Solo, which is the same thing a lone opponent has
    /// always done.
    /// </summary>
    public FlightOrders OrdersFor(Combatant member)
    {
        for (int i = 0; i < _members.Count; i++)
            if (ReferenceEquals(_members[i], member)) return _orders[i];

        return FlightOrders.Solo;
    }

    /// <summary>Call once per sim tick, before the pilots fly.</summary>
    public void Update(Match match, Arena arena, double dt)
    {
        _sinceHandover += dt;
        _shakenFor = Math.Max(0.0, _shakenFor - dt);
        Target = PickTarget(match);

        int alive = AliveCount;

        // Somebody just went down. Everyone breaks off and goes high.
        if (_lastAlive >= 0 && alive < _lastAlive) _shakenFor = ShakenSeconds;
        _lastAlive = alive;

        if (Target is null || alive == 0)
        {
            Engaged = null;
            ClearOrders();
            return;
        }

        if (_shakenFor > 0)
        {
            // Nobody presses while the flight is regrouping. Everyone still gets a
            // station, so they climb away together instead of milling about.
            Engaged = null;
            AssignStations(arena);
            return;
        }

        // A lone survivor is not a flight. Holding a perch with nobody left to
        // cover is just hiding, so the last one standing goes in.
        if (alive == 1)
        {
            Engaged = FirstAlive();
            ClearOrders();
            return;
        }

        ChooseEngaged(dt);
        AssignStations(arena);
    }

    // --- Who goes in --------------------------------------------------------

    private void ChooseEngaged(double dt)
    {
        if (Engaged is not null && !Engaged.IsAlive) Engaged = null;

        if (Engaged is null)
        {
            Engaged = BestAttacker(exclude: null);
            _sinceHandover = 0;
            _outOfPosition = 0;
            return;
        }

        _outOfPosition = HasThePosition(Engaged) ? 0 : _outOfPosition + dt;

        if (_outOfPosition < OutOfPositionS || _sinceHandover < HandoverHoldS) return;

        var relief = BestAttacker(exclude: Engaged);
        if (relief is null) return;

        // Only hand over to somebody genuinely better placed. Swapping into an
        // equally hopeless position gets both pilots out of the fight at once.
        if (AttackScore(relief) >= AttackScore(Engaged) * 0.8) return;

        Engaged = relief;
        _sinceHandover = 0;
        _outOfPosition = 0;
    }

    /// <summary>Is this pilot still in a position worth pressing? Lower is better.</summary>
    private bool HasThePosition(Combatant c)
    {
        if (Target is null) return false;

        var s = c.State;
        if (s.OnFire) return false;
        if (WorstSystem(s) < 0.45) return false;

        Vec2 toTarget = Target.State.Position - s.Position;
        if (toTarget.Length > 700.0) return false;

        return Math.Abs(Angles.Delta(s.Theta, toTarget.Angle)) < 1.4;
    }

    /// <summary>
    /// How well placed a pilot is to attack, as a cost in meters. Range, plus a
    /// penalty for having the nose in the wrong place, plus a large one for being
    /// shot up. A damaged pilot should be the one who goes high and recovers.
    /// </summary>
    private double AttackScore(Combatant c)
    {
        if (Target is null) return double.MaxValue;

        var s = c.State;
        Vec2 toTarget = Target.State.Position - s.Position;
        double angleOff = Math.Abs(Angles.Delta(s.Theta, toTarget.Angle));

        return toTarget.Length
             + angleOff * 260.0
             + (1.0 - WorstSystem(s)) * 900.0
             + (s.OnFire ? 4000.0 : 0.0);
    }

    private Combatant? BestAttacker(Combatant? exclude)
    {
        Combatant? best = null;
        double bestScore = double.MaxValue;

        foreach (var m in _members)
        {
            if (!m.IsAlive || ReferenceEquals(m, exclude)) continue;

            double score = AttackScore(m);
            if (score < bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    // --- Where everyone else waits ------------------------------------------

    private void AssignStations(Arena arena)
    {
        Vec2 target = Target!.State.Position;

        // Perch on the side the engaged pilot is NOT on, so the target is boxed
        // instead of queued up behind. Whichever way it breaks, somebody is there.
        //
        // With nobody engaged, which is the regroup after a loss, perch ahead of
        // where the target is going instead. That is the moment it will try to run,
        // and arriving where it is headed is worth more than following it.
        double side = Engaged is not null
            ? (target.X - Engaged.State.Position.X >= 0 ? 1.0 : -1.0)
            : (Target!.State.Velocity.X >= 0 ? 1.0 : -1.0);

        int rank = 0;

        for (int i = 0; i < _members.Count; i++)
        {
            var m = _members[i];

            if (!m.IsAlive) { _orders[i] = FlightOrders.Solo; continue; }

            if (ReferenceEquals(m, Engaged))
            {
                _orders[i] = new FlightOrders(FlightRole.Engaged, target);
                continue;
            }

            double x = target.X + side * (PerchOffsetM + rank * 150.0);
            double y = target.Y + PerchHeightM + rank * 70.0;

            // Clamp the perch into the box. A station outside the arena is a
            // station a pilot will fly out of the field trying to reach.
            x = Angles.Clamp(x, 220.0, Math.Max(240.0, arena.WidthM - 220.0));
            y = Angles.Clamp(y, 260.0, Math.Max(280.0, arena.CeilingM - 140.0));

            _orders[i] = new FlightOrders(FlightRole.Supporting, new Vec2(x, y));
            rank++;
        }
    }

    // --- Odds and ends ------------------------------------------------------

    private Combatant? PickTarget(Match match)
    {
        Vec2 centre = Vec2.Zero;
        int n = 0;

        foreach (var m in _members)
        {
            if (!m.IsAlive) continue;
            centre += m.State.Position;
            n++;
        }

        if (n == 0) return null;
        centre /= n;

        Combatant? best = null;
        double bestSq = double.MaxValue;

        foreach (var c in match.Combatants)
        {
            if (c.Team == Team || !c.IsAlive) continue;

            double dSq = (c.State.Position - centre).LengthSquared;
            if (dSq < bestSq) { bestSq = dSq; best = c; }
        }
        return best;
    }

    private Combatant? FirstAlive()
    {
        foreach (var m in _members) if (m.IsAlive) return m;
        return null;
    }

    private void ClearOrders()
    {
        for (int i = 0; i < _orders.Count; i++) _orders[i] = FlightOrders.Solo;
    }

    private static double WorstSystem(AircraftState s)
        => Math.Min(Math.Min(s.EngineHealth, s.WingHealth), s.TailHealth);
}
