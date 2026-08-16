using System.Collections.Generic;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// All the sound. Two continuous voices that track the aircraft, and a pool of
/// one-shots for everything that happens.
///
/// The hit marker is the reason this exists. Tracers show where your fire went,
/// but only the marker tells you it landed, and without that a dogfight is
/// guesswork with a trigger.
/// </summary>
public sealed partial class GameAudio : Node
{
    private const int OneShotVoices = 12;

    private AudioStreamPlayer _engine = null!;
    private AudioStreamPlayer _wind = null!;
    private readonly List<AudioStreamPlayer> _oneShots = new();
    private int _nextVoice;

    private AudioStreamWav _gunShot = null!;
    private AudioStreamWav _hitMarker = null!;
    private AudioStreamWav _killMarker = null!;
    private AudioStreamWav _takingHit = null!;
    private AudioStreamWav _gunJam = null!;
    private AudioStreamWav _explosion = null!;

    // Edge detection, so a continuous state only fires a sound once.
    private int _lastAmmo = int.MaxValue;
    private bool _wasJammed;
    private bool _wasAlive = true;
    private double _gunCooldown;

    public static GameAudio Create()
    {
        var audio = new GameAudio { Name = "Audio" };

        audio._engine = new AudioStreamPlayer
        {
            Name = "Engine",
            Stream = Synth.EngineLoop(),
            VolumeDb = -9f,
            Autoplay = false,
        };
        audio._wind = new AudioStreamPlayer
        {
            Name = "Wind",
            Stream = Synth.WindLoop(),
            VolumeDb = -30f,
            Autoplay = false,
        };
        audio.AddChild(audio._engine);
        audio.AddChild(audio._wind);

        for (int i = 0; i < OneShotVoices; i++)
        {
            var voice = new AudioStreamPlayer { Name = $"Voice{i}" };
            audio.AddChild(voice);
            audio._oneShots.Add(voice);
        }

        audio._gunShot = Synth.GunShot();
        audio._hitMarker = Synth.HitMarker();
        audio._killMarker = Synth.KillMarker();
        audio._takingHit = Synth.TakingHit();
        audio._gunJam = Synth.GunJam();
        audio._explosion = Synth.Explosion();

        return audio;
    }

    public override void _Ready()
    {
        _engine.Play();
        _wind.Play();
    }

    /// <summary>
    /// Drive the continuous voices and fire anything that just happened. Called
    /// once per rendered frame with the player's aircraft.
    /// </summary>
    public void Update(AircraftState s, AircraftSpec spec, IReadOnlyList<HitEvent> hits, double delta)
    {
        UpdateEngine(s);
        UpdateWind(s, spec);
        UpdateGuns(s, delta);
        UpdateHits(hits);

        if (_wasAlive && !s.IsAlive) Play(_explosion, -4f);
        _wasAlive = s.IsAlive;
    }

    private void UpdateEngine(AircraftState s)
    {
        // A rotary has no throttle in the modern sense, but RPM still tracks power,
        // and a starving or shot-up engine drops in pitch as well as volume. That
        // fall is the first warning a pilot gets that the engine is going.
        double power = s.Throttle * (1.0 - s.FuelStarvation) * s.EngineHealth;
        if (!s.IsAlive) power = 0;

        _engine.PitchScale = (float)Mathf.Lerp(0.55, 1.85, Mathf.Clamp(power, 0.0, 1.0));
        _engine.VolumeDb = power < 0.02f ? -60f : (float)Mathf.Lerp(-20.0, -7.0, power);
    }

    private void UpdateWind(AircraftState s, AircraftSpec spec)
    {
        // Slipstream rises with speed and becomes a real warning at the top end,
        // which is the closest a WW1 aircraft had to an overspeed horn.
        double fast = Mathf.Clamp(s.Airspeed / 95.0, 0.0, 1.4);
        _wind.VolumeDb = (float)Mathf.Lerp(-34.0, -11.0, fast);
        _wind.PitchScale = (float)Mathf.Lerp(0.75, 1.5, fast);
    }

    private void UpdateGuns(AircraftState s, double delta)
    {
        _gunCooldown -= delta;

        // Ammo dropping is the honest signal that a round actually left the gun.
        if (s.Ammo < _lastAmmo && _gunCooldown <= 0)
        {
            Play(_gunShot, -13f, 0.94f + (float)GD.RandRange(0.0, 0.12));
            _gunCooldown = 0.055;
        }
        _lastAmmo = s.Ammo;

        if (s.GunJammed && !_wasJammed) Play(_gunJam, -8f);
        _wasJammed = s.GunJammed;
    }

    private void UpdateHits(IReadOnlyList<HitEvent> hits)
    {
        foreach (var hit in hits)
        {
            bool byPlayer = hit.ShooterIndex == 0;
            bool onPlayer = hit.VictimIndex == 0;

            if (hit.Fatal)
            {
                Play(byPlayer ? _killMarker : _explosion, -5f);
                continue;
            }

            if (byPlayer)
            {
                // Pitch carries how good the shot was. A solid close hit rings
                // higher and louder than a long one that barely scratched.
                float quality = (float)hit.Effectiveness;
                Play(_hitMarker, Mathf.Lerp(-17f, -7f, quality), Mathf.Lerp(0.78f, 1.12f, quality));
            }
            else if (onPlayer)
            {
                Play(_takingHit, -7f);
            }
        }
    }

    private void Play(AudioStream stream, float volumeDb, float pitch = 1f)
    {
        var voice = _oneShots[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _oneShots.Count;

        voice.Stream = stream;
        voice.VolumeDb = volumeDb;
        voice.PitchScale = pitch;
        voice.Play();
    }
}
