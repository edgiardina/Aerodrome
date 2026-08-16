using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// Not assertions. These dump telemetry so a human can read what the model does.
/// They are the text-mode version of the M1 tuning overlay.
/// </summary>
public class DiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public void PrintSpecEnvelope()
    {
        foreach (var spec in new[] { AircraftSpec.SopwithCamel, AircraftSpec.CamelArcade })
        {
            output.WriteLine($"=== {spec.Name} ===");
            output.WriteLine($"  aspect ratio    {spec.AspectRatio:F2}");
            output.WriteLine($"  induced k       {spec.InducedDragFactor:F4}");
            output.WriteLine($"  stall alpha     {Angles.ToDegrees(spec.StallAlphaRad):F1} deg");
            output.WriteLine($"  stall speed     {spec.StallSpeedSeaLevel:F1} m/s  ({spec.StallSpeedSeaLevel * 3.6:F0} km/h)");
            output.WriteLine($"  corner speed    {spec.CornerSpeedSeaLevel:F1} m/s  ({spec.CornerSpeedSeaLevel * 3.6:F0} km/h)");

            double rho = Atmosphere.SeaLevelDensity;
            output.WriteLine("   v(m/s)  slew(deg/s)  turn radius(m)  loop dia(m)  KE height(m)");
            for (double v = 20; v <= 90; v += 10)
            {
                double omega = FlightModel.MaxSlewRate(v, rho, spec);
                double radius = omega > 1e-6 ? v / omega : double.NaN;
                double keHeight = v * v / (2 * Atmosphere.Gravity);
                output.WriteLine($"   {v,5:F0}  {Angles.ToDegrees(omega),10:F1}  {radius,14:F0}  {2 * radius,11:F0}  {keHeight,12:F0}");
            }
            output.WriteLine("");
        }
    }

    [Fact]
    public void TraceImmelmann()
    {
        var rig = new Rig().Spawn(3000, 1500, 0.0, 70.0);
        output.WriteLine($"spec: {rig.Spec.Name}");
        output.WriteLine($"start  alt {rig.Altitude:F0}  spd {rig.Speed:F1}  Eh {rig.EnergyHeight:F0}");
        output.WriteLine("   t     hdg    alt    spd     Eh   alpha    G   inv  stall");

        double nextPrint = 0;
        rig.ResetSweep();
        double target = 0;
        for (int i = 0; i < (int)(12 * FlightModel.TickRate) && rig.State.IsAlive; i++)
        {
            if (Math.Abs(rig.Sweep) >= Math.PI) break;
            target += 0.9 * FlightModel.FixedDt;
            double cmd = Angles.Wrap0To2Pi(rig.State.Theta + rig.State.CanopySign * Math.Min(target, 0.6));
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = cmd });
            target = Math.Max(0, target - Math.Abs(rig.State.SlewRateRad) * FlightModel.FixedDt);

            if (rig.Time >= nextPrint)
            {
                var s = rig.State;
                output.WriteLine($" {rig.Time,4:F1}  {rig.HeadingDeg,6:F0}  {rig.Altitude,5:F0}  {rig.Speed,5:F1}  {rig.EnergyHeight,5:F0}  " +
                                 $"{Angles.ToDegrees(s.Alpha),5:F1}  {s.LoadFactor,4:F1}   {(s.IsInverted ? "Y" : "-")}    {(s.IsStalled ? "Y" : "-")}");
                nextPrint += 0.25;
            }
        }

        output.WriteLine($"after half loop: sweep {Angles.ToDegrees(rig.Sweep):F0} deg  hdg {rig.HeadingDeg:F0}  " +
                         $"alt {rig.Altitude:F0}  spd {rig.Speed:F1}  inverted {rig.State.IsInverted}  alive {rig.State.IsAlive}");

        rig.HalfRoll();
        output.WriteLine($"after roll:      hdg {rig.HeadingDeg:F0}  alt {rig.Altitude:F0}  spd {rig.Speed:F1}  " +
                         $"inverted {rig.State.IsInverted}  facingRight {rig.FacingRight}");
    }

    [Fact]
    public void TraceSplitS()
    {
        var rig = new Rig().Spawn(3000, 1500, 0.0, 45.0);
        output.WriteLine($"start  alt {rig.Altitude:F0}  spd {rig.Speed:F1}  facingRight {rig.FacingRight}");

        rig.HalfRoll();
        output.WriteLine($"after roll:      inverted {rig.State.IsInverted}  canopy {rig.State.CanopySign}  hdg {rig.HeadingDeg:F0}");

        bool done = rig.Pull(Math.PI);
        output.WriteLine($"after half loop: completed {done}  sweep {Angles.ToDegrees(rig.Sweep):F0} deg  hdg {rig.HeadingDeg:F0}  " +
                         $"alt {rig.Altitude:F0}  spd {rig.Speed:F1}  inverted {rig.State.IsInverted}  " +
                         $"facingRight {rig.FacingRight}  alive {rig.State.IsAlive}");
    }

    [Fact]
    public void TraceInvertedEngineStarve()
    {
        var rig = new Rig().Spawn(3000, 2000, 0.0, 55.0);
        rig.HalfRoll();
        output.WriteLine($"rolled inverted: inverted {rig.State.IsInverted}");
        output.WriteLine("   t   starve   spd    alt");
        for (int i = 0; i < (int)(6 * FlightModel.TickRate) && rig.State.IsAlive; i++)
        {
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = rig.State.Theta });
            if (i % 60 == 0)
                output.WriteLine($" {rig.Time,4:F1}   {rig.State.FuelStarvation,5:F2}  {rig.Speed,5:F1}  {rig.Altitude,5:F0}");
        }
    }
}
