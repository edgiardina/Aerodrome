using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// The ground you are fighting over: aerodromes, a ruined village, a front line,
/// and the observation balloons that were the reason most of these fights happened.
///
/// Built in code rather than downloaded, deliberately. The free low-poly prop
/// libraries are flat-shaded cartoon geometry, and this game has a photoscanned
/// Sopwith Camel in the foreground. A Kenney tent behind it makes BOTH look wrong:
/// the aeroplane starts to read as out of place and the tent reads as a
/// placeholder. Matching the art direction matters more than the polygon count of
/// any individual hut.
///
/// Period note: a Quonset hut is American and dates from 1941. The 1916 British
/// original is the NISSEN hut, and that is what these are. Same corrugated arch,
/// twenty-five years earlier.
///
/// == The thing that makes this hard ==
///
/// The camera looks along -Z, level, from about 260 m back. So an object's height
/// on screen is set by the ANGLE below the camera, and the camera is usually 300 m
/// up. Anything closer than about 1100 m is below the bottom of the frame at
/// normal fighting altitude, however tall it is.
///
/// The first attempt put the whole aerodrome 95 m behind the play plane at true
/// scale. It rendered perfectly and was completely invisible, because it was
/// 40 degrees below a 15 degree half-angle lens. It only existed if you flew at
/// treetop height.
///
/// So there are three bands, and each one is sized for the distance it sits at:
///
///   NEAR  (95 m back, true scale)   Only visible down low. The reward for it.
///   FAR   (2 km back, seven times)  The skyline. What you see from up high.
///   AIR   (700 m back, balloons)    At your altitude, so always in shot.
///
/// The far band is drawn seven times life size. That is the same cheat the ridge
/// layers already use: at 2.3 km a Nissen hut is a quarter of a pixel, and what
/// the eye wants there is a readable silhouette, not a correct one.
///
/// Nothing here is collidable and nothing here is in Core. It is scenery.
/// </summary>
public static class Scenery
{
    /// <summary>True scale, just behind the play plane. Only in frame down low.</summary>
    private const float NearZ = -95f;

    /// <summary>The skyline band. Stands clear of the ridge layer in front of it.</summary>
    private const float FarZ = -2000f;
    private const float FarScale = 7f;

    /// <summary>Balloons live at your altitude, near enough to read.</summary>
    private const float AirZ = -700f;
    private const float AirScale = 2.5f;

    private static readonly Color Canvas = new(0.42f, 0.40f, 0.31f);
    private static readonly Color Corrugate = new(0.30f, 0.31f, 0.27f);
    private static readonly Color Timber = new(0.24f, 0.20f, 0.15f);
    private static readonly Color Sandbag = new(0.38f, 0.35f, 0.26f);
    private static readonly Color Masonry = new(0.44f, 0.42f, 0.38f);
    private static readonly Color Mud = new(0.20f, 0.17f, 0.13f);
    private static readonly Color Doped = new(0.62f, 0.58f, 0.46f);
    private static readonly Color BalloonSkin = new(0.54f, 0.49f, 0.36f);

    // The far band sits in the aerial perspective of the ridge behind it, so it is
    // washed toward the haze. Painting it in the near band's colours would make the
    // horizon look like a cardboard cut-out pasted on.
    private static readonly Color FarStone = new(0.38f, 0.41f, 0.37f);
    private static readonly Color FarCanvas = new(0.40f, 0.42f, 0.35f);
    private static readonly Color FarDark = new(0.31f, 0.34f, 0.30f);

    public static void Build(Node3D parent, Arena arena, int seed)
    {
        var root = new Node3D { Name = "Scenery" };
        parent.AddChild(root);

        var rng = new Random(seed);
        float width = (float)arena.WidthM;

        BuildSkyline(root, rng, width);
        BuildGroundDetail(root, rng, width);
        BuildBalloons(root, width);
    }

    // --- The skyline: what you see from fighting altitude ----------------------

    private static void BuildSkyline(Node3D parent, Random rng, float width)
    {
        var band = Layer(parent, "Skyline", FarZ, FarScale);

        // Local space is divided by the scale, so lay everything out in real metres
        // and convert once.
        float L(float worldX) => worldX / FarScale;

        // Two aerodromes, one at each end of the line. Hangars only: at this range
        // a bell tent is invisible and a hangar is a shape.
        SkylineField(band, L(width * 0.13f), rng);
        SkylineField(band, L(width * 0.90f), rng);

        // The church tower, broken. Every photograph of this war has one.
        var tower = new Node3D { Position = new Vector3(L(width * 0.45f), 0f, 0f) };
        band.AddChild(tower);
        Box(tower, new Vector3(7f, 24f, 7f), new Vector3(0f, 12f, 0f), FarStone);
        Box(tower, new Vector3(2.8f, 5f, 7.4f), new Vector3(2.2f, 26f, 0f), FarDark);

        // The village around it.
        for (int i = 0; i < 14; i++)
        {
            float hx = L(width * 0.45f) + (float)(rng.NextDouble() - 0.5) * 46f;
            float h = 5f + (float)rng.NextDouble() * 6f;
            Box(band, new Vector3(6f + (float)rng.NextDouble() * 5f, h, 7f),
                new Vector3(hx, h * 0.5f, (float)(rng.NextDouble() - 0.5) * 8f), FarStone);
        }

        // Smoke standing over the line. Static geometry, never simulated: it is
        // 2 km away and it does not need to move.
        for (int i = 0; i < 5; i++)
            SmokeColumn(band, L(width * (0.58f + i * 0.09f)), 0f,
                        14f + (float)rng.NextDouble() * 12f, FarDark);

        // Shattered wood on the skyline, which is the detail that dates it.
        for (int i = 0; i < 70; i++)
        {
            float tx = L((float)rng.NextDouble() * width);
            float h = 2.5f + (float)rng.NextDouble() * 3.5f;
            Taper(band, 0.35f, 0.08f, h, new Vector3(tx, h * 0.5f, (float)(rng.NextDouble() - 0.5) * 10f),
                  FarDark, segments: 4);
        }
    }

    private static void SkylineField(Node3D parent, float x, Random rng)
    {
        var field = new Node3D { Position = new Vector3(x, 0f, 0f) };
        parent.AddChild(field);

        // Hangar sheds, gable ended.
        for (int i = 0; i < 3; i++)
        {
            float hx = -14f + i * 14f;
            Box(field, new Vector3(12f, 7f, 9f), new Vector3(hx, 3.5f, 0f), FarCanvas);

            var roof = new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(12.6f, 3.2f, 9.4f) },
                Position = new Vector3(hx, 8.6f, 0f),
                MaterialOverride = Matte(FarDark),
            };
            field.AddChild(roof);
        }

        // Nissen arches beside them, sunk to the axis so half shows.
        for (int i = 0; i < 4; i++)
            Arch(field, radius: 2.2f, length: 9f, new Vector3(24f + i * 11f, 0f, 4f), FarDark);
    }

    // --- The ground detail: the reward for flying low --------------------------

    private static void BuildGroundDetail(Node3D parent, Random rng, float width)
    {
        var band = Layer(parent, "GroundDetail", NearZ, 1f);

        Aerodrome(band, rng, width * 0.13f);
        Aerodrome(band, rng, width * 0.90f, hutCount: 2, mirrored: true);
        Village(band, rng, width * 0.45f);
        FrontLine(band, rng, width * 0.62f, width * 0.84f);
        ShellCraters(band, rng, width * 0.55f, width * 0.88f, 40);
        Trees(band, rng, width);
    }

    private static void Aerodrome(Node3D parent, Random rng, float x,
                                  int hutCount = 3, bool mirrored = false)
    {
        var field = new Node3D { Name = "Aerodrome", Position = new Vector3(x, 0f, 0f) };
        parent.AddChild(field);

        float dir = mirrored ? -1f : 1f;

        // The landing ground: a mown strip, just above zero so it does not fight
        // the ground plane for the same pixels.
        Box(field, new Vector3(360f, 0.5f, 40f), new Vector3(0f, 0.25f, 6f),
            new Color(0.34f, 0.36f, 0.22f));

        for (int i = 0; i < hutCount; i++)
            NissenHut(field, dir * (-70f + i * 34f), -14f, length: 26f, radius: 4.6f);

        // A Bessonneau: the canvas-and-timber hangar the RFC actually used in the
        // field. The open mouth is what makes it read as a hangar and not a shed.
        Bessonneau(field, dir * 62f, -10f);

        for (int i = 0; i < 4; i++)
            Cone(field, 3.4f, 4.4f,
                 new Vector3(dir * (-104f - i * 13f), 0f, -26f + (float)rng.NextDouble() * 6f), Canvas);

        ParkedAircraft(field, dir * 18f, 2f);
        ParkedAircraft(field, dir * 40f, 4f);
        Windsock(field, dir * 96f, 14f);

        for (int i = 0; i < 7; i++)
            Cylinder(field, 0.55f, 1.5f,
                     new Vector3(dir * (78f + i % 4 * 1.4f), 0.75f, -22f + i / 4 * 1.4f),
                     new Color(0.28f, 0.30f, 0.24f));
    }

    /// <summary>Corrugated iron half-arch, blockwork ends, a stove pipe.</summary>
    private static void NissenHut(Node3D parent, float x, float z, float length, float radius)
    {
        var hut = new Node3D { Name = "NissenHut", Position = new Vector3(x, 0f, z) };
        parent.AddChild(hut);

        Arch(hut, radius, length, Vector3.Zero, Corrugate);

        Box(hut, new Vector3(0.6f, radius, radius * 2f), new Vector3(length * 0.5f, radius * 0.5f, 0f), Masonry);
        Box(hut, new Vector3(0.6f, radius, radius * 2f), new Vector3(-length * 0.5f, radius * 0.5f, 0f), Masonry);
        Box(hut, new Vector3(0.4f, 2.0f, 1.0f), new Vector3(-length * 0.5f - 0.2f, 1.0f, 1.2f), Timber);

        Cylinder(hut, 0.16f, 2.2f, new Vector3(length * 0.25f, radius + 0.9f, 0f), Corrugate);
    }

    /// <summary>
    /// A cylinder laid along X and sunk to its own axis, so exactly half of it
    /// stands above ground. Faceted on purpose: it reads as corrugation.
    /// </summary>
    private static void Arch(Node3D parent, float radius, float length, Vector3 position, Color colour)
        => parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 14,
                Rings = 1,
            },
            Position = position,
            Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
            MaterialOverride = Matte(colour),
        });

    private static void Bessonneau(Node3D parent, float x, float z)
    {
        var shed = new Node3D { Name = "Bessonneau", Position = new Vector3(x, 0f, z) };
        parent.AddChild(shed);

        const float w = 34f, h = 8.5f, d = 20f;
        var gable = Matte(Canvas);

        for (int side = -1; side <= 1; side += 2)
            shed.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, 0.4f, d * 0.62f) },
                Position = new Vector3(0f, h - 1.4f, side * d * 0.26f),
                Rotation = new Vector3(side * 0.30f, 0f, 0f),
                MaterialOverride = gable,
            });

        // Three walls. The mouth is the point.
        Box(shed, new Vector3(0.7f, h - 1.5f, d), new Vector3(-w * 0.5f, (h - 1.5f) * 0.5f, 0f), Canvas);
        Box(shed, new Vector3(0.7f, h - 1.5f, d), new Vector3(w * 0.5f, (h - 1.5f) * 0.5f, 0f), Canvas);
        Box(shed, new Vector3(w, h - 1.5f, 0.7f), new Vector3(0f, (h - 1.5f) * 0.5f, -d * 0.5f), Canvas);

        // Dark inside, so the opening is not a flat painted rectangle.
        Box(shed, new Vector3(w - 3f, h - 2.5f, 1.0f), new Vector3(0f, (h - 2.5f) * 0.5f, -d * 0.42f),
            new Color(0.07f, 0.07f, 0.07f));

        for (int i = -2; i <= 2; i++)
            Box(shed, new Vector3(0.45f, h - 1.5f, 0.45f),
                new Vector3(i * w * 0.22f, (h - 1.5f) * 0.5f, d * 0.5f), Timber);
    }

    private static void ParkedAircraft(Node3D parent, float x, float z)
    {
        var kite = new Node3D
        {
            Name = "Parked",
            Position = new Vector3(x, 0f, z),
            Rotation = new Vector3(0f, 0f, 0.16f),   // nose high on the tailskid
        };
        parent.AddChild(kite);

        Box(kite, new Vector3(7.0f, 0.9f, 0.9f), new Vector3(0f, 1.5f, 0f), Doped);
        Box(kite, new Vector3(1.6f, 0.12f, 8.4f), new Vector3(1.4f, 2.5f, 0f), Doped);
        Box(kite, new Vector3(1.5f, 0.12f, 7.8f), new Vector3(1.3f, 1.1f, 0f), Doped);
        Box(kite, new Vector3(1.4f, 0.10f, 3.0f), new Vector3(-3.2f, 1.7f, 0f), Doped);
        Box(kite, new Vector3(1.1f, 1.3f, 0.10f), new Vector3(-3.3f, 2.3f, 0f), Doped);
        Cylinder(kite, 0.62f, 0.22f, new Vector3(1.2f, 0.62f, 1.3f), Timber);
        Cylinder(kite, 0.62f, 0.22f, new Vector3(1.2f, 0.62f, -1.3f), Timber);
    }

    private static void Windsock(Node3D parent, float x, float z)
    {
        var pole = new Node3D { Name = "Windsock", Position = new Vector3(x, 0f, z) };
        parent.AddChild(pole);

        Cylinder(pole, 0.14f, 9f, new Vector3(0f, 4.5f, 0f), Timber);

        // Streaming downwind, which in this arena is from the right. It agrees with
        // Arena.Wind by construction.
        pole.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.35f, Height = 3.4f, RadialSegments = 8 },
            Position = new Vector3(-1.9f, 8.4f, 0f),
            Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
            MaterialOverride = Matte(new Color(0.72f, 0.36f, 0.16f)),
        });
    }

    private static void Village(Node3D parent, Random rng, float x)
    {
        var village = new Node3D { Name = "Village", Position = new Vector3(x, 0f, 0f) };
        parent.AddChild(village);

        Box(village, new Vector3(7f, 22f, 7f), new Vector3(0f, 11f, -8f), Masonry);
        Box(village, new Vector3(7.4f, 3f, 3.2f), new Vector3(0f, 23.2f, -8f), new Color(0.38f, 0.36f, 0.33f));
        Box(village, new Vector3(2.6f, 4f, 7.4f), new Vector3(2.2f, 24.4f, -8f), new Color(0.36f, 0.34f, 0.31f));

        for (int i = 0; i < 9; i++)
        {
            float hx = (float)(rng.NextDouble() - 0.5) * 190f;
            float w = 8f + (float)rng.NextDouble() * 7f;
            float h = 4.5f + (float)rng.NextDouble() * 4f;
            float z = -4f - (float)rng.NextDouble() * 16f;

            Box(village, new Vector3(w, h, 9f), new Vector3(hx, h * 0.5f, z), Masonry);
            Box(village, new Vector3(1.0f, h * 1.7f, 9f), new Vector3(hx + w * 0.5f, h * 0.85f, z),
                new Color(0.40f, 0.38f, 0.34f));
        }

        Box(village, new Vector3(300f, 0.4f, 7f), new Vector3(0f, 0.2f, 12f),
            new Color(0.33f, 0.30f, 0.24f));
    }

    private static void FrontLine(Node3D parent, Random rng, float from, float to)
    {
        var line = new Node3D { Name = "FrontLine" };
        parent.AddChild(line);

        Box(line, new Vector3(to - from, 0.6f, 62f), new Vector3((from + to) * 0.5f, 0.3f, -6f), Mud);

        // Traversed, not straight. The zigzag is the whole silhouette of a trench,
        // and it is why they were dug that way.
        Trench(line, rng, from + 20f, to - 20f, 8f, Sandbag);
        Trench(line, rng, from + 55f, to - 60f, -26f, new Color(0.34f, 0.31f, 0.24f));

        for (int i = 0; i < 90; i++)
            Box(line, new Vector3(0.16f, 1.5f, 0.16f),
                new Vector3(from + (float)rng.NextDouble() * (to - from), 0.75f,
                            -6f + (float)(rng.NextDouble() - 0.5) * 22f), Timber);

        for (int i = 0; i < 3; i++)
            SmokeColumn(line, from + (0.25f + i * 0.28f) * (to - from), -18f,
                        46f + (float)rng.NextDouble() * 30f, new Color(0.24f, 0.22f, 0.21f));
    }

    private static void Trench(Node3D parent, Random rng, float from, float to, float z, Color colour)
    {
        var material = Matte(colour);
        const float step = 11f;
        int i = 0;

        for (float x = from; x < to; x += step, i++)
        {
            float offset = i % 2 == 0 ? 0f : 4.5f;
            float h = 1.1f + (float)rng.NextDouble() * 0.5f;

            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(step * 1.04f, h, 4.5f) },
                Position = new Vector3(x, h * 0.5f, z + offset),
                MaterialOverride = material,
            });
        }
    }

    private static void ShellCraters(Node3D parent, Random rng, float from, float to, int count)
    {
        var craters = new Node3D { Name = "Craters" };
        parent.AddChild(craters);

        // Seen from the side, a crater IS its rim.
        for (int i = 0; i < count; i++)
        {
            float r = 2.5f + (float)rng.NextDouble() * 5f;
            Taper(craters, r, r * 0.75f, 0.9f,
                  new Vector3(from + (float)rng.NextDouble() * (to - from), 0.45f,
                              14f - (float)rng.NextDouble() * 40f),
                  new Color(0.26f, 0.23f, 0.18f), segments: 9);
        }
    }

    /// <summary>Bare, broken off partway up, never a whole one.</summary>
    private static void Trees(Node3D parent, Random rng, float arenaWidth)
    {
        var wood = new Node3D { Name = "Trees" };
        parent.AddChild(wood);

        for (int i = 0; i < 120; i++)
        {
            float h = 3.5f + (float)rng.NextDouble() * 8f;
            Taper(wood, 0.38f, 0.10f, h,
                  new Vector3((float)rng.NextDouble() * arenaWidth * 1.1f - arenaWidth * 0.05f,
                              h * 0.5f, 16f - (float)rng.NextDouble() * 34f),
                  new Color(0.17f, 0.15f, 0.12f), segments: 5,
                  tilt: (float)(rng.NextDouble() - 0.5) * 0.25f);
        }
    }

    // --- The balloons ---------------------------------------------------------

    private static void BuildBalloons(Node3D parent, float width)
    {
        var band = Layer(parent, "Balloons", AirZ, AirScale);

        // At your own altitude and only 700 m back, so they are in shot for most of
        // a fight. Worth more than decoration: balloons were what the scouts were
        // sent up to burn or to protect, so one hanging over the line says what the
        // aeroplanes are for. Scenery for now.
        Balloon(band, width * 0.28f / AirScale, 300f / AirScale);
        Balloon(band, width * 0.71f / AirScale, 255f / AirScale);
    }

    private static void Balloon(Node3D parent, float x, float y)
    {
        var kite = new Node3D { Name = "Balloon", Position = new Vector3(x, y, 0f) };
        parent.AddChild(kite);

        var skin = Matte(BalloonSkin);

        kite.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 5.2f, Height = 22f, RadialSegments = 12, Rings = 4 },
            Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
            MaterialOverride = skin,
        });

        // The tail lobes that kept it steady, and the reason anyone can tell a
        // Drachen from a barrage balloon.
        for (int i = -1; i <= 1; i += 2)
            kite.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 2.6f, Height = 4.4f, RadialSegments = 8, Rings = 4 },
                Position = new Vector3(-11.5f, 2.2f, i * 2.6f),
                MaterialOverride = skin,
            });

        Box(kite, new Vector3(1.8f, 1.6f, 1.8f), new Vector3(0f, -8.5f, 0f), Timber);

        // The cable, all the way to the winch. A balloon with no tether has escaped.
        Box(kite, new Vector3(0.14f, y - 8.5f, 0.14f), new Vector3(0f, -8.5f - (y - 8.5f) * 0.5f, 0f),
            new Color(0.14f, 0.14f, 0.14f));
    }

    private static void SmokeColumn(Node3D parent, float x, float z, float height, Color tint)
    {
        var column = new Node3D { Name = "Smoke", Position = new Vector3(x, 0f, z) };
        parent.AddChild(column);

        int puffs = Math.Max(3, (int)(height / 6f));

        for (int i = 0; i < puffs; i++)
        {
            float t = i / (float)puffs;
            float r = height * 0.05f + t * height * 0.15f;

            column.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r, Height = r * 1.7f, RadialSegments = 7, Rings = 4 },
                Position = new Vector3(t * height * 0.2f, 3f + t * height, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(tint, 0.32f - t * 0.21f),
                    DisableReceiveShadows = true,
                },
            });
        }
    }

    // --- Primitives -----------------------------------------------------------

    private static Node3D Layer(Node3D parent, string name, float z, float scale)
    {
        var layer = new Node3D
        {
            Name = name,
            Position = new Vector3(0f, 0f, z),
            Scale = Vector3.One * scale,
        };
        parent.AddChild(layer);
        return layer;
    }

    private static void Box(Node3D parent, Vector3 size, Vector3 position, Color colour)
        => parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = Matte(colour),
        });

    private static void Cylinder(Node3D parent, float radius, float height, Vector3 position, Color colour)
        => Taper(parent, radius, radius, height, position, colour, segments: 8);

    private static void Cone(Node3D parent, float radius, float height, Vector3 position, Color colour)
        => Taper(parent, radius, 0.05f, height, position + new Vector3(0f, height * 0.5f, 0f),
                 colour, segments: 9);

    private static void Taper(Node3D parent, float bottom, float top, float height,
                              Vector3 position, Color colour, int segments, float tilt = 0f)
        => parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = top,
                BottomRadius = bottom,
                Height = height,
                RadialSegments = segments,
                Rings = 1,
            },
            Position = position,
            Rotation = new Vector3(0f, 0f, tilt),
            MaterialOverride = Matte(colour),
        });

    private static StandardMaterial3D Matte(Color colour) => new()
    {
        AlbedoColor = colour,
        Roughness = 1.0f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };
}
