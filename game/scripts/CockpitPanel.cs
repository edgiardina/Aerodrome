using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// The instrument board, across the bottom of the screen.
///
/// This replaces a column of numbers with something you read the way a pilot
/// reads: by where the needles are pointing, not by parsing digits. A glance tells
/// you the aeroplane is slow and low and the engine is sick, without reading a
/// single figure.
///
/// The instruments are the ones a 1917 scout actually carried, and no others. No
/// artificial horizon, because nobody had one. No gunsight cross painted on the
/// glass. The compass is missing on purpose too: the whole game happens on one
/// vertical plane, so a heading rose would only ever read east or west.
///
/// What is left is airspeed, height, engine revolutions, oil, fuel, and a bubble
/// to tell you which way up you are. That last one is doing real work here, since
/// inversion is a state you have to notice and fix.
///
/// The text telemetry still exists behind F3. This is what is on screen by default.
/// </summary>
public sealed partial class CockpitPanel : Control
{
    private const float BoardHeight = 152f;
    private const float DialRadius = 38f;
    private const float DialSpacing = 100f;

    private static readonly Color Wood = new(0.16f, 0.11f, 0.07f);
    private static readonly Color WoodLip = new(0.29f, 0.20f, 0.12f);
    private static readonly Color Brass = new(0.72f, 0.58f, 0.28f);
    private static readonly Color BrassDark = new(0.38f, 0.30f, 0.14f);
    private static readonly Color Face = new(0.09f, 0.09f, 0.08f);
    private static readonly Color Dial = new(0.82f, 0.79f, 0.70f);
    private static readonly Color DialDim = new(0.48f, 0.46f, 0.41f);
    private static readonly Color Needle = new(0.94f, 0.92f, 0.86f);
    private static readonly Color RedLine = new(0.82f, 0.24f, 0.18f);
    private static readonly Color Warn = new(0.98f, 0.72f, 0.20f);

    // A 270 degree sweep starting at the lower left, which is how nearly every
    // round instrument since has been laid out.
    private const float SweepStart = 2.356f;    // 135 degrees, measured down-left
    private const float SweepAngle = 4.712f;    // 270 degrees clockwise

    private SimRunner _sim = null!;
    private Font _font = null!;

    /// <summary>Needles have mass. They lag and they overshoot, and it reads as real.</summary>
    private double _asi, _alt, _rpm, _oil, _fuel, _bubble;

    public static CockpitPanel Create(SimRunner sim) => new()
    {
        Name = "CockpitPanel",
        _sim = sim,
        MouseFilter = MouseFilterEnum.Ignore,
        AnchorRight = 1,
        AnchorBottom = 1,
    };

    public override void _Ready() => _font = ThemeDB.FallbackFont;

    public override void _Process(double delta)
    {
        var s = _sim.Player.State;
        var spec = _sim.Player.Spec;

        // Exponential approach with a time constant, so the lag is the same at any
        // refresh rate. A per-frame lerp would make the needles quicker on a faster
        // machine, which is exactly the class of bug the sim layer avoids.
        double k = 1.0 - Math.Exp(-delta / 0.10);
        double kSlow = 1.0 - Math.Exp(-delta / 0.35);

        _asi += (s.Airspeed * 3.6 - _asi) * k;
        _alt += (s.Position.Y - _alt) * k;
        _rpm += (s.Throttle * (1.0 - s.FuelStarvation) * s.EngineHealth - _rpm) * k;
        _oil += (s.EngineHealth - _oil) * kSlow;
        _fuel += (s.FuelSystemHealth * (1.0 - s.FuelStarvation) - _fuel) * kSlow;

        // The bubble runs off which way up the aeroplane is, blended through the
        // roll so it swings across rather than jumping.
        double target = s.CanopySign >= 0 ? 0.0 : 1.0;
        _bubble += (target - _bubble) * (1.0 - Math.Exp(-delta / 0.18));

        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        float top = size.Y - BoardHeight;

        DrawBoard(size, top);

        var spec = _sim.Player.Spec;
        var s = _sim.Player.State;

        float centre = size.X * 0.5f;
        float y = top + 64f;

        // Six instruments, laid out around the centre of the board.
        float x = centre - DialSpacing * 2.5f;

        AirspeedDial(new Vector2(x, y), spec);
        x += DialSpacing;
        AltimeterDial(new Vector2(x, y));
        x += DialSpacing;
        TachometerDial(new Vector2(x, y));
        x += DialSpacing;
        PressureDial(new Vector2(x, y), "OIL", _oil, s.EngineHealth < 0.5);
        x += DialSpacing;
        PressureDial(new Vector2(x, y), "FUEL", _fuel, s.FuelStarvation > 0.2);
        x += DialSpacing;
        BankIndicator(new Vector2(x, y), s);

        // Well clear of the outer dials. At the first spacing these sat on top of
        // the A.S.I. and the bank card.
        float outboard = DialSpacing * 2.5f + DialRadius + 46f;
        AmmoCounter(new Vector2(centre - outboard, y), s, spec);
        GunTemperature(new Vector2(centre + outboard, y), s);

        // The maker's plate. It is the only thing on screen that says which
        // aeroplane you are sitting in, and with F8 swapping sides that matters.
        MakersPlate(new Vector2(centre - outboard - 96f, y), spec);
    }

    private void MakersPlate(Vector2 centre, AircraftSpec spec)
    {
        var box = new Rect2(centre.X - 62f, centre.Y - 20f, 124f, 40f);

        DrawRect(box, new Color(0.10f, 0.08f, 0.05f), true);
        DrawRect(box, BrassDark, false, 1.4f);

        // Two lines, because "Sopwith Camel (arcade)" does not fit on one and the
        // bracket is a development detail the cockpit does not need.
        string name = spec.Name;
        int bracket = name.IndexOf('(');
        if (bracket > 0) name = name[..bracket].TrimEnd();

        int split = name.LastIndexOf(' ');
        string top = split > 0 ? name[..split] : name;
        string bottom = split > 0 ? name[(split + 1)..] : "";

        DrawString(_font, new Vector2(box.Position.X, box.Position.Y + 17f), top,
                   HorizontalAlignment.Center, box.Size.X, 11, Brass);
        DrawString(_font, new Vector2(box.Position.X, box.Position.Y + 32f), bottom,
                   HorizontalAlignment.Center, box.Size.X, 13, Dial);
    }

    // --- The board ------------------------------------------------------------

    private void DrawBoard(Vector2 size, float top)
    {
        // Varnished plywood, darker toward the bottom, with a turned lip along the
        // top edge. The lip is what makes it read as a coaming and not a letterbox.
        DrawRect(new Rect2(0, top, size.X, BoardHeight), Wood, true);
        DrawRect(new Rect2(0, top, size.X, 4f), WoodLip, true);
        DrawRect(new Rect2(0, top + 4f, size.X, 2f), new Color(0.09f, 0.06f, 0.04f), true);

        // Grain. A few long strokes at slightly different tones is enough at this
        // size, and it stops the panel reading as a flat brown rectangle.
        for (int i = 0; i < 14; i++)
        {
            float gy = top + 12f + i * 8.6f;
            var tone = new Color(WoodLip, 0.055f + (i % 3) * 0.02f);
            DrawLine(new Vector2(0, gy), new Vector2(size.X, gy + (i % 2 == 0 ? 1.5f : -1.5f)), tone, 1.4f);
        }
    }

    // --- The instruments ------------------------------------------------------

    private void AirspeedDial(Vector2 centre, AircraftSpec spec)
    {
        // 0 to 450 km/h. The dial has to run past the never-exceed speed or the red
        // arc that matters most falls off the end of the scale and never draws,
        // which is what the first version did: 360 on a 320 dial.
        const double max = 450.0;
        Bezel(centre, "A.S.I.", "km/h");

        for (int v = 0; v <= 450; v += 50)
            Tick(centre, v / max, v % 100 == 0, (v / 10).ToString());

        // The stall at the bottom and the never-exceed speed at the top, both in
        // red, the way a real dial marks its two ends. The top one has to be here:
        // a dive limit you cannot see coming is an ambush, not a decision.
        ArcBand(centre, 0.0, spec.StallSpeedSeaLevel * 3.6 / max, RedLine);
        ArcBand(centre, spec.NeverExceedSpeed * 3.6 / max, 1.0, RedLine);

        var s = _sim.Player.State;
        Pointer(centre, _asi / max, s.IsOverspeed ? RedLine : Needle);
    }

    private void AltimeterDial(Vector2 centre)
    {
        // One turn is 1000 m, which is the whole arena and then some.
        const double max = 1000.0;
        Bezel(centre, "ALT", "metres");

        for (int v = 0; v <= 1000; v += 100)
            Tick(centre, v / max, v % 200 == 0, (v / 100).ToString());

        // The ground, and the height at which you have run out of room to pull.
        ArcBand(centre, 0.0, 80.0 / max, RedLine);

        Pointer(centre, _alt / max, Needle);
    }

    private void TachometerDial(Vector2 centre)
    {
        // A rotary's revolutions, scaled off delivered power. 1250 rpm is about
        // where a Clerget lived, so the red line goes just above it.
        const double max = 1500.0;
        double rpm = 250.0 + _rpm * 1050.0;

        Bezel(centre, "REVS", "r.p.m.");

        for (int v = 0; v <= 1500; v += 250)
            Tick(centre, v / max, true, (v / 100).ToString());

        ArcBand(centre, 1300.0 / max, 1.0, RedLine);
        Pointer(centre, rpm / max, Needle);
    }

    private void PressureDial(Vector2 centre, string label, double fraction, bool alarming)
    {
        Bezel(centre, label, "");

        for (int i = 0; i <= 4; i++)
            Tick(centre, i / 4.0, i % 2 == 0, "");

        ArcBand(centre, 0.0, 0.25, RedLine);
        Pointer(centre, fraction, alarming ? Warn : Needle);
    }

    /// <summary>
    /// A bank card: the aeroplane seen from behind, rolling as you roll.
    ///
    /// This is the only instrument here that is load bearing. Inversion is a state
    /// the pilot has to notice and then spend a roll to fix, and the only cue up to
    /// now was the aeroplane itself, which is genuinely hard to read side-on in the
    /// middle of a fight.
    ///
    /// It started as a spirit level, which is the period-correct answer and was
    /// useless: a bubble sliding along a tube says how far off you are, not which
    /// way up. A little aeroplane rolling upside down says it instantly.
    /// </summary>
    private void BankIndicator(Vector2 centre, AircraftState s)
    {
        Bezel(centre, "BANK", "");

        // A fixed horizon behind the aeroplane, so the roll has something to be
        // measured against.
        DrawLine(centre + new Vector2(-DialRadius * 0.62f, 0f),
                 centre + new Vector2(DialRadius * 0.62f, 0f),
                 new Color(DialDim, 0.55f), 1.2f, true);

        float roll = (float)s.RollAngle;
        var right = new Vector2(Mathf.Cos(roll), Mathf.Sin(roll));
        var up = new Vector2(-right.Y, right.X);

        var colour = s.IsInverted ? Warn : Needle;

        // Wings, fin and fuselage, drawn rear-on.
        DrawLine(centre - right * 17f, centre + right * 17f, colour, 3.2f, true);
        DrawLine(centre, centre - up * 9f, colour, 2.4f, true);
        DrawCircle(centre, 3.0f, colour);

        // Wingtip marks, so a 180 degree roll is not ambiguous.
        DrawCircle(centre + right * 17f, 2.0f, colour);

        DrawString(_font, centre + new Vector2(-DialRadius, 25f),
                   s.IsInverted ? "INVERTED" : "upright",
                   HorizontalAlignment.Center, DialRadius * 2f, 9,
                   s.IsInverted ? Warn : DialDim);
    }

    /// <summary>
    /// The ammunition counter, as a little window of drum numbers rather than a
    /// dial. Vickers guns had a mechanical counter and it looked like this.
    /// </summary>
    private void AmmoCounter(Vector2 centre, AircraftState s, AircraftSpec spec)
    {
        var box = new Rect2(centre.X - 26f, centre.Y - 14f, 52f, 26f);

        DrawRect(box, new Color(0.05f, 0.05f, 0.05f), true);
        DrawRect(box, BrassDark, false, 1.6f);

        var colour = s.Ammo == 0 ? RedLine : s.Ammo < spec.AmmoRounds * 0.2 ? Warn : Dial;
        DrawString(_font, new Vector2(box.Position.X, box.Position.Y + 19f), $"{s.Ammo:D3}",
                   HorizontalAlignment.Center, box.Size.X, 17, colour);

        DrawString(_font, new Vector2(box.Position.X, box.End.Y + 14f), "ROUNDS",
                   HorizontalAlignment.Center, box.Size.X, 9, DialDim);
    }

    /// <summary>
    /// Gun heat, as a thermometer rather than a dial. It is the one reading that
    /// only ever goes one way and then comes back, so a bar says it better.
    /// </summary>
    private void GunTemperature(Vector2 centre, AircraftState s)
    {
        var tube = new Rect2(centre.X - 8f, centre.Y - 26f, 16f, 52f);

        DrawRect(tube, new Color(0.05f, 0.05f, 0.05f), true);
        DrawRect(tube, BrassDark, false, 1.6f);

        float h = (float)Math.Clamp(s.GunHeat, 0, 1) * (tube.Size.Y - 4f);
        var colour = s.GunHeat > 0.7 ? RedLine : s.GunHeat > 0.35 ? Warn : new Color(0.45f, 0.72f, 0.55f);
        DrawRect(new Rect2(tube.Position.X + 2f, tube.End.Y - 2f - h, tube.Size.X - 4f, h), colour, true);

        // The mark where jams start. Below a quarter heat the guns never jam, so
        // that line is genuinely useful and not decoration.
        float mark = tube.End.Y - 2f - 0.25f * (tube.Size.Y - 4f);
        DrawLine(new Vector2(tube.Position.X - 3f, mark), new Vector2(tube.End.X + 3f, mark), Dial, 1f);

        string label = s.GunJammed ? "JAM" : "GUNS";
        DrawString(_font, new Vector2(centre.X - 26f, tube.End.Y + 14f), label,
                   HorizontalAlignment.Center, 52f, 9, s.GunJammed ? RedLine : DialDim);
    }

    // --- Dial furniture -------------------------------------------------------

    private void Bezel(Vector2 centre, string label, string units)
    {
        DrawCircle(centre, DialRadius, Face);
        DrawArc(centre, DialRadius, 0, Mathf.Tau, 32, Brass, 2.6f, true);
        DrawArc(centre, DialRadius - 2.4f, 0, Mathf.Tau, 32, BrassDark, 1.2f, true);

        DrawString(_font, centre + new Vector2(-DialRadius, DialRadius + 15f), label,
                   HorizontalAlignment.Center, DialRadius * 2f, 10, Dial);

        if (units.Length > 0)
            DrawString(_font, centre + new Vector2(-DialRadius, 20f), units,
                       HorizontalAlignment.Center, DialRadius * 2f, 8, DialDim);
    }

    private void Tick(Vector2 centre, double fraction, bool major, string label)
    {
        float angle = SweepStart + (float)fraction * SweepAngle;
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        float inner = DialRadius - (major ? 9f : 5f);
        DrawLine(centre + dir * inner, centre + dir * (DialRadius - 3f),
                 major ? Dial : DialDim, major ? 1.8f : 1.1f, true);

        // No numerals at the two ends of the sweep. Both sit at the bottom of the
        // dial either side of the units line, and all three landed on top of each
        // other: the A.S.I. read "0km/h320".
        if (!major || label.Length == 0 || fraction < 0.06 || fraction > 0.94) return;

        DrawString(_font, centre + dir * (DialRadius - 17f) + new Vector2(-9f, 4f), label,
                   HorizontalAlignment.Center, 18f, 9, DialDim);
    }

    /// <summary>A coloured band round the rim: a red line, or a stall arc.</summary>
    private void ArcBand(Vector2 centre, double from, double to, Color colour)
    {
        from = Math.Clamp(from, 0, 1);
        to = Math.Clamp(to, 0, 1);
        if (to <= from) return;

        DrawArc(centre, DialRadius - 5.5f,
                SweepStart + (float)from * SweepAngle,
                SweepStart + (float)to * SweepAngle,
                18, colour, 2.4f, true);
    }

    private void Pointer(Vector2 centre, double fraction, Color colour)
    {
        // Needles peg at the stops rather than wrapping round, which is what a real
        // one does and which stops a fast dive making the ASI read zero.
        float angle = SweepStart + (float)Math.Clamp(fraction, 0.0, 1.0) * SweepAngle;
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var perp = new Vector2(-dir.Y, dir.X);

        // A tapered needle with a counterweighted tail.
        DrawColoredPolygon(
            new[]
            {
                centre + dir * (DialRadius - 7f),
                centre + perp * 2.4f - dir * 6f,
                centre - perp * 2.4f - dir * 6f,
            }, colour);

        DrawLine(centre, centre - dir * 9f, colour, 2.6f, true);
        DrawCircle(centre, 3.4f, BrassDark);
        DrawCircle(centre, 1.8f, Brass);
    }
}
