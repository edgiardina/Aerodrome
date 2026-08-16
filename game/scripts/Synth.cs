using System;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Builds every sound in the game as raw PCM at startup.
///
/// Nothing is loaded from disk. That is partly practical, since the asset licence
/// question is still open and synthesised audio has no licence at all, and partly
/// because a rotary engine is a pitched pulse train, which is far easier to
/// generate correctly than to find a clean loop of.
/// </summary>
public static class Synth
{
    public const int SampleRate = 22050;

    /// <summary>
    /// A Clerget rotary at idle, as a loop. Pitch is scaled at playback to track
    /// RPM, so this only has to be the right texture, not the right speed.
    ///
    /// Nine cylinders firing on alternate revolutions gives a hard, uneven bark
    /// rather than a smooth hum. That unevenness is the whole character of the
    /// sound, so the harmonics are deliberately not in tidy proportion.
    /// </summary>
    public static AudioStreamWav EngineLoop()
    {
        const double baseHz = 46.0;
        int samples = (int)(SampleRate / baseHz * 16);   // 16 cycles, loops cleanly
        var pcm = new float[samples];
        var rng = new Random(1234);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double phase = t * baseHz % 1.0;

            // Sharp exhaust pulse: quick attack, exponential decay.
            double pulse = Math.Exp(-phase * 7.0) * 0.9;

            // Uneven firing order and mechanical clatter on top.
            pulse += Math.Sin(t * baseHz * 2.0 * Math.PI * 3.0) * 0.16;
            pulse += Math.Sin(t * baseHz * 2.0 * Math.PI * 4.7) * 0.09;
            pulse += (rng.NextDouble() - 0.5) * 0.07;

            pcm[i] = (float)Math.Clamp(pulse * 0.55, -1.0, 1.0);
        }

        return Wav(pcm, loop: true);
    }

    /// <summary>Slipstream. Filtered noise, volume scaled by airspeed at playback.</summary>
    public static AudioStreamWav WindLoop()
    {
        int samples = SampleRate * 2;
        var pcm = new float[samples];
        var rng = new Random(99);
        double low = 0, band = 0;

        for (int i = 0; i < samples; i++)
        {
            double white = rng.NextDouble() * 2.0 - 1.0;
            low += (white - low) * 0.06;            // rumble
            band += (white - low - band) * 0.35;    // hiss
            pcm[i] = (float)Math.Clamp((low * 1.3 + band * 0.5) * 0.5, -1.0, 1.0);
        }

        CrossfadeEnds(pcm, 2000);
        return Wav(pcm, loop: true);
    }

    /// <summary>One round leaving the gun. A short cracking bark, not a bang.</summary>
    public static AudioStreamWav GunShot()
    {
        int samples = (int)(SampleRate * 0.11);
        var pcm = new float[samples];
        var rng = new Random(7);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 42.0);
            double crack = (rng.NextDouble() * 2.0 - 1.0) * 0.8;
            double body = Math.Sin(t * 2.0 * Math.PI * 160.0) * 0.5
                        + Math.Sin(t * 2.0 * Math.PI * 90.0) * 0.35;
            pcm[i] = (float)Math.Clamp((crack + body) * env * 0.7, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    /// <summary>
    /// The hit marker. A short bright two-tone ping, rising.
    ///
    /// This is the single most important sound in the game. Tracers tell you where
    /// your fire went; only this tells you it connected, and without it a fight is
    /// guesswork. Deliberately tonal and high so it cuts through engine and wind,
    /// which are both broadband and low.
    /// </summary>
    public static AudioStreamWav HitMarker()
    {
        int samples = (int)(SampleRate * 0.13);
        var pcm = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 26.0);
            double sweep = 1180.0 + t * 2600.0;      // rises, so it reads as "good"
            double tone = Math.Sin(t * 2.0 * Math.PI * sweep) * 0.6
                        + Math.Sin(t * 2.0 * Math.PI * sweep * 1.5) * 0.25;
            pcm[i] = (float)Math.Clamp(tone * env, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    /// <summary>A kill. Lower, longer, and it falls instead of rising.</summary>
    public static AudioStreamWav KillMarker()
    {
        int samples = (int)(SampleRate * 0.45);
        var pcm = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 6.0);
            double sweep = 760.0 - t * 420.0;
            double tone = Math.Sin(t * 2.0 * Math.PI * sweep) * 0.55
                        + Math.Sin(t * 2.0 * Math.PI * sweep * 0.5) * 0.35;
            pcm[i] = (float)Math.Clamp(tone * env, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    /// <summary>
    /// Taking a round. A dull metallic thump on canvas and wire, with no tone to
    /// it, so it can never be mistaken for the hit marker.
    /// </summary>
    public static AudioStreamWav TakingHit()
    {
        int samples = (int)(SampleRate * 0.22);
        var pcm = new float[samples];
        var rng = new Random(313);
        double low = 0;

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 17.0);
            double white = rng.NextDouble() * 2.0 - 1.0;
            low += (white - low) * 0.22;
            double thud = Math.Sin(t * 2.0 * Math.PI * 78.0) * 0.6;
            pcm[i] = (float)Math.Clamp((low * 1.4 + thud) * env * 0.85, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    /// <summary>Guns falling silent mid-burst. A hard mechanical clunk.</summary>
    public static AudioStreamWav GunJam()
    {
        int samples = (int)(SampleRate * 0.18);
        var pcm = new float[samples];
        var rng = new Random(555);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 30.0);
            double clunk = Math.Sin(t * 2.0 * Math.PI * 118.0) * 0.7
                         + (rng.NextDouble() - 0.5) * 0.5;
            pcm[i] = (float)Math.Clamp(clunk * env, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    /// <summary>Airframe destruction. Broadband, long, with a tearing edge.</summary>
    public static AudioStreamWav Explosion()
    {
        int samples = (int)(SampleRate * 1.1);
        var pcm = new float[samples];
        var rng = new Random(2024);
        double low = 0, mid = 0;

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 3.4);
            double white = rng.NextDouble() * 2.0 - 1.0;
            low += (white - low) * 0.03;
            mid += (white - mid) * 0.16;
            pcm[i] = (float)Math.Clamp((low * 2.2 + mid * 0.8) * env, -1.0, 1.0);
        }

        return Wav(pcm);
    }

    // --- Plumbing -----------------------------------------------------------

    /// <summary>Wrap float samples as 16-bit mono PCM, which is what Godot wants.</summary>
    private static AudioStreamWav Wav(float[] pcm, bool loop = false)
    {
        var bytes = new byte[pcm.Length * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            short value = (short)(Math.Clamp(pcm[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = bytes,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? pcm.Length : 0,
        };
    }

    /// <summary>Blend the tail into the head so a noise loop has no audible seam.</summary>
    private static void CrossfadeEnds(float[] pcm, int fade)
    {
        fade = Math.Min(fade, pcm.Length / 4);
        for (int i = 0; i < fade; i++)
        {
            float t = (float)i / fade;
            pcm[i] = pcm[i] * t + pcm[pcm.Length - fade + i] * (1f - t);
        }
    }
}
