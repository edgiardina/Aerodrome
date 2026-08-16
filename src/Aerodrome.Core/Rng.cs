namespace Aerodrome.Core;

/// <summary>
/// A small deterministic generator. Core never touches System.Random, because
/// System.Random's algorithm is not guaranteed stable across runtimes and this sim
/// has to replay identically. Same seed, same sequence, forever.
///
/// A mutable struct on purpose: pass it by ref and it allocates nothing.
/// This is xoshiro128**, which is fast, small, and has good statistical quality.
/// </summary>
public struct Rng
{
    private uint _a, _b, _c, _d;

    public Rng(uint seed)
    {
        // SplitMix32 to spread a single seed across the whole state.
        _a = Mix(ref seed);
        _b = Mix(ref seed);
        _c = Mix(ref seed);
        _d = Mix(ref seed);

        // Never allow the all-zero state.
        if ((_a | _b | _c | _d) == 0) _a = 0x9E3779B9u;

        for (int i = 0; i < 8; i++) NextUInt();
    }

    private static uint Mix(ref uint seed)
    {
        seed += 0x9E3779B9u;
        uint z = seed;
        z = (z ^ (z >> 16)) * 0x21F0AAADu;
        z = (z ^ (z >> 15)) * 0x735A2D97u;
        return z ^ (z >> 15);
    }

    public uint NextUInt()
    {
        uint result = Rotate(_b * 5u, 7) * 9u;
        uint t = _b << 9;

        _c ^= _a;
        _d ^= _b;
        _b ^= _c;
        _a ^= _d;
        _c ^= t;
        _d = Rotate(_d, 11);

        return result;
    }

    private static uint Rotate(uint x, int k) => (x << k) | (x >> (32 - k));

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt() >> 8) * (1.0 / 16777216.0);

    /// <summary>Uniform in [-1, 1].</summary>
    public double NextSigned() => NextDouble() * 2.0 - 1.0;

    /// <summary>Uniform integer in [0, exclusiveMax).</summary>
    public int NextInt(int exclusiveMax)
        => exclusiveMax <= 0 ? 0 : (int)(NextUInt() % (uint)exclusiveMax);

    public bool Chance(double probability) => NextDouble() < probability;
}
