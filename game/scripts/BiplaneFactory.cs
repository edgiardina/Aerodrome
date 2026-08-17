using Godot;

namespace Aerodrome.Game;

/// <summary>
/// A placeholder Sopwith Camel built from boxes, sized from the real aircraft.
/// It exists so M1 can tune flight feel before any art lands. The real glTF model
/// drops into the same node slot later.
///
/// The camera looks along -Z at the XY plane, so we see the aircraft from the side
/// and the wings are edge on. That is correct for this game, and it means a roll
/// sweeps the wings through the screen, which is the whole visual payoff of using
/// 3D models on a fixed plane.
/// </summary>
public static class BiplaneFactory
{
    // Sopwith Camel F.1: 5.71 m long, 8.53 m span, 2.59 m tall.
    private static readonly Color Khaki = new(0.40f, 0.38f, 0.26f);
    private static readonly Color Linen = new(0.78f, 0.73f, 0.58f);
    private static readonly Color Cowl = new(0.32f, 0.31f, 0.30f);
    private static readonly Color Dark = new(0.10f, 0.09f, 0.08f);
    private static readonly Color Wood = new(0.34f, 0.21f, 0.10f);
    private static readonly Color Rubber = new(0.09f, 0.09f, 0.10f);

    /// <summary>The parts of the airframe that move. The view drives these directly.</summary>
    public sealed class Parts
    {
        public required Node3D Root { get; init; }
        public required Node3D Propeller { get; init; }
        public required Node3D AileronLeft { get; init; }
        public required Node3D AileronRight { get; init; }
        public required Node3D Elevator { get; init; }
        public required Node3D Rudder { get; init; }
    }

    /// <summary>
    /// Where a processed model lives if one has been made.
    ///
    /// Inside the Godot project, not in assets/export next door, because Godot
    /// only imports what lives under res:// and silently sees nothing outside it.
    /// </summary>
    private static string ModelPath(string name) => $"res://models/{name}.glb";

    /// <summary>
    /// Use the real model if it has been prepared, otherwise the boxes.
    ///
    /// The fallback is not a nicety. The Sketchfab Camel is CC-BY and fine to use,
    /// but it has to be downloaded with an account and run through
    /// tools/prepare-model.ps1 first, so a fresh clone has no model in it. The game
    /// must still run.
    /// </summary>
    public static Parts Build(Color teamColor, string modelName = "camel")
    {
        var imported = TryBuildImported(teamColor, modelName);
        if (imported is not null) return imported;

        return BuildPlaceholder(teamColor);
    }

    private static Parts? TryBuildImported(Color teamColor, string modelName)
    {
        string path = ModelPath(modelName);
        if (!ResourceLoader.Exists(path)) return null;

        var scene = ResourceLoader.Load<PackedScene>(path);
        if (scene is null) return null;

        var root = scene.Instantiate<Node3D>();
        root.Name = "Airframe";

        // The pipeline names the spinning part "Propeller". If it could not find
        // one, fall back to an empty node so the view has something to rotate.
        var propeller = root.FindChild("Propeller", recursive: true, owned: false) as Node3D;
        if (propeller is null)
        {
            propeller = new Node3D { Name = "Propeller" };
            root.AddChild(propeller);
            GD.Print("[model] no Propeller node in the .glb, so it will not spin");
        }

        // A real model has no separate control surfaces, so give the view empty
        // nodes to drive. Deflection is lost until the model is split up further.
        Node3D Stub(string name)
        {
            var node = new Node3D { Name = name };
            root.AddChild(node);
            return node;
        }

        // Team colour still has to read at a glance, so tint the whole airframe
        // very slightly rather than leaving both sides identical.
        Tint(root, teamColor);

        return new Parts
        {
            Root = root,
            Propeller = propeller,
            AileronLeft = Stub("AileronLeft"),
            AileronRight = Stub("AileronRight"),
            Elevator = Stub("Elevator"),
            Rudder = Stub("Rudder"),
        };
    }

    private static void Tint(Node node, Color teamColor)
    {
        if (node is MeshInstance3D mesh)
        {
            // Barely there. An unshaded overlay reads far stronger than its alpha
            // suggests, and at 0.18 it painted half the aeroplane blue and buried
            // the roundels and the paintwork that make the model worth having.
            var overlay = new StandardMaterial3D
            {
                AlbedoColor = new Color(teamColor, 0.055f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            mesh.MaterialOverlay = overlay;
        }

        foreach (var child in node.GetChildren()) Tint(child, teamColor);
    }

    private static Parts BuildPlaceholder(Color teamColor)
    {
        var root = new Node3D { Name = "Airframe" };

        var khaki = Solid(Khaki);
        var linen = Solid(Linen);
        var cowl = Solid(Cowl, roughness: 0.45f, metallic: 0.6f);
        var dark = Solid(Dark);
        var wood = Solid(Wood);
        var rubber = Solid(Rubber);
        var team = Solid(teamColor);

        // Fuselage. Three tapering sections instead of one slab, because the side
        // profile is the whole silhouette in this game: deep at the cockpit,
        // narrowing to the sternpost.
        Box(root, "FuselageFore", new Vector3(1.90f, 0.86f, 0.74f), new Vector3(1.30f, 0.02f, 0f), khaki);
        Box(root, "FuselageMid", new Vector3(1.60f, 0.74f, 0.66f), new Vector3(-0.35f, -0.02f, 0f), khaki);
        // Tapers to 0.34, not 0.20. Any thinner and the fin has nothing to sit on
        // and reads as a slab floating off the back of the aeroplane.
        Taper(root, "FuselageAft", 0.66f, 0.34f, 2.20f, new Vector3(-2.20f, -0.02f, 0f), khaki);

        // The rotary's cowl is a drum, and its open bottom is unmistakable.
        Drum(root, "Cowl", radius: 0.47f, length: 0.86f, new Vector3(2.38f, 0.06f, 0f), cowl);
        Box(root, "CowlLip", new Vector3(0.10f, 0.30f, 0.66f), new Vector3(2.80f, -0.24f, 0f), dark);

        // Wings, with stagger: the upper is set forward of the lower, which is the
        // single most recognisable thing about a Camel from the side.
        Box(root, "UpperWing", new Vector3(1.42f, 0.11f, 8.53f), new Vector3(0.62f, 1.06f, 0f), khaki);
        Box(root, "UpperWingEdge", new Vector3(0.16f, 0.13f, 8.53f), new Vector3(1.31f, 1.06f, 0f), dark);
        Box(root, "LowerWing", new Vector3(1.34f, 0.11f, 7.92f), new Vector3(0.06f, -0.42f, 0f), linen);
        Box(root, "LowerWingEdge", new Vector3(0.15f, 0.12f, 7.92f), new Vector3(0.71f, -0.42f, 0f), dark);

        // Cabane struts to the centre section, splayed as they actually were.
        Strut(root, "CabaneFwdL", new Vector3(0.95f, 0.42f, 0.32f), new Vector3(0.72f, 1.02f, 0.40f), dark);
        Strut(root, "CabaneFwdR", new Vector3(0.95f, 0.42f, -0.32f), new Vector3(0.72f, 1.02f, -0.40f), dark);
        Strut(root, "CabaneAftL", new Vector3(0.28f, 0.42f, 0.32f), new Vector3(0.44f, 1.02f, 0.40f), dark);
        Strut(root, "CabaneAftR", new Vector3(0.28f, 0.42f, -0.32f), new Vector3(0.44f, 1.02f, -0.40f), dark);

        // Interplane struts, one pair each side, raked with the stagger.
        foreach (int side in new[] { 1, -1 })
        {
            Strut(root, $"StrutFwd{side}", new Vector3(0.36f, -0.38f, 2.58f * side),
                  new Vector3(0.92f, 1.02f, 2.58f * side), dark);
            Strut(root, $"StrutAft{side}", new Vector3(-0.34f, -0.38f, 2.58f * side),
                  new Vector3(0.22f, 1.02f, 2.58f * side), dark);

            // Flying wires. Thin, dark, and they read as the wire cage that made a
            // biplane look like a biplane.
            Strut(root, $"WireA{side}", new Vector3(0.10f, -0.38f, 1.05f * side),
                  new Vector3(0.90f, 1.02f, 2.50f * side), dark, 0.035f);
            Strut(root, $"WireB{side}", new Vector3(0.86f, -0.38f, 2.50f * side),
                  new Vector3(0.30f, 1.02f, 1.05f * side), dark, 0.035f);
        }

        // Tail. The fin pointing up is the strongest inversion cue in the silhouette,
        // so it has to be clearly readable, but it also has to sit ON the fuselage.
        Box(root, "Tailplane", new Vector3(0.76f, 0.08f, 2.45f), new Vector3(-2.78f, -0.02f, 0f), linen);
        Box(root, "Fin", new Vector3(0.62f, 0.66f, 0.08f), new Vector3(-2.92f, 0.30f, 0f), khaki);
        Box(root, "FinStripe", new Vector3(0.22f, 0.44f, 0.10f), new Vector3(-3.06f, 0.32f, 0f), team);

        // Control surfaces. Each hangs off a pivot at its hinge line so the view can
        // just set a rotation. Watching these move is most of what makes a maneuver
        // read as flown instead of as a model being rotated.
        var aileronLeft = Hinged(root, "AileronLeft", new Vector3(-0.32f, 1.05f, 2.15f),
                                 new Vector3(0.44f, 0.07f, 2.55f), linen);
        var aileronRight = Hinged(root, "AileronRight", new Vector3(-0.32f, 1.05f, -2.15f),
                                  new Vector3(0.44f, 0.07f, 2.55f), linen);
        var elevator = Hinged(root, "Elevator", new Vector3(-3.16f, -0.02f, 0f),
                              new Vector3(0.38f, 0.07f, 2.35f), linen);
        var rudder = Hinged(root, "Rudder", new Vector3(-3.23f, 0.30f, 0f),
                            new Vector3(0.34f, 0.62f, 0.07f), khaki);

        // Cockpit, set just behind the hump that houses the twin Vickers.
        Box(root, "GunHump", new Vector3(0.92f, 0.22f, 0.52f), new Vector3(1.34f, 0.50f, 0f), khaki);
        Box(root, "VickersL", new Vector3(1.00f, 0.11f, 0.11f), new Vector3(1.40f, 0.62f, 0.13f), dark);
        Box(root, "VickersR", new Vector3(1.00f, 0.11f, 0.11f), new Vector3(1.40f, 0.62f, -0.13f), dark);
        Box(root, "Cockpit", new Vector3(0.56f, 0.30f, 0.56f), new Vector3(0.60f, 0.48f, 0f), dark);
        Box(root, "Coaming", new Vector3(0.64f, 0.10f, 0.64f), new Vector3(0.60f, 0.62f, 0f), wood);
        Box(root, "Headrest", new Vector3(0.24f, 0.22f, 0.44f), new Vector3(0.22f, 0.56f, 0f), khaki);

        // Exhaust stubs poking out of the cowl.
        Box(root, "ExhaustL", new Vector3(0.44f, 0.10f, 0.10f), new Vector3(2.02f, -0.30f, 0.26f), cowl);
        Box(root, "ExhaustR", new Vector3(0.44f, 0.10f, 0.10f), new Vector3(2.02f, -0.30f, -0.26f), cowl);

        // Undercarriage. Wheels below are the other half of the inversion read, and
        // the V struts and spreader bar are a big part of the head-on silhouette.
        Strut(root, "GearFwdL", new Vector3(1.22f, -0.36f, 0.30f), new Vector3(0.92f, -1.02f, 0.62f), dark);
        Strut(root, "GearFwdR", new Vector3(1.22f, -0.36f, -0.30f), new Vector3(0.92f, -1.02f, -0.62f), dark);
        Strut(root, "GearAftL", new Vector3(0.56f, -0.36f, 0.30f), new Vector3(0.92f, -1.02f, 0.62f), dark);
        Strut(root, "GearAftR", new Vector3(0.56f, -0.36f, -0.30f), new Vector3(0.92f, -1.02f, -0.62f), dark);
        Box(root, "Spreader", new Vector3(0.30f, 0.09f, 1.34f), new Vector3(0.92f, -1.00f, 0f), linen);
        Wheel(root, "WheelL", new Vector3(0.92f, -1.04f, 0.66f), rubber);
        Wheel(root, "WheelR", new Vector3(0.92f, -1.04f, -0.66f), rubber);

        // Tail skid.
        Strut(root, "TailSkid", new Vector3(-3.05f, -0.14f, 0f), new Vector3(-3.34f, -0.58f, 0f), wood, 0.09f);

        // Propeller. Its own node so it can spin about the long axis.
        var propeller = new Node3D { Name = "Propeller", Position = new Vector3(2.88f, 0.05f, 0f) };
        root.AddChild(propeller);
        Box(propeller, "Blade", new Vector3(0.07f, 2.55f, 0.20f), Vector3.Zero, wood);
        Box(propeller, "Spinner", new Vector3(0.22f, 0.26f, 0.26f), new Vector3(0.10f, 0f, 0f), cowl);

        return new Parts
        {
            Root = root,
            Propeller = propeller,
            AileronLeft = aileronLeft,
            AileronRight = aileronRight,
            Elevator = elevator,
            Rudder = rudder,
        };
    }

    /// <summary>
    /// A control surface on a pivot at its hinge line. The mesh hangs aft of the
    /// pivot, so rotating the pivot swings the trailing edge the way a real one does.
    /// </summary>
    private static Node3D Hinged(Node parent, string name, Vector3 hinge, Vector3 size, Material mat)
    {
        var pivot = new Node3D { Name = name, Position = hinge };
        parent.AddChild(pivot);
        Box(pivot, "Surface", size, new Vector3(-size.X * 0.5f, 0f, 0f), mat);
        return pivot;
    }

    /// <summary>
    /// A flat chevron for Far View. At full zoom-out an aircraft is a couple of
    /// pixels wide, so the model is swapped for something you can actually read.
    /// </summary>
    public static Node3D BuildIcon(Color teamColor)
    {
        var root = new Node3D { Name = "Icon", Visible = false };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = teamColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = teamColor,
            EmissionEnergyMultiplier = 1.4f,
        };

        // Two bars forming a ">" that points along +X, so it shows heading.
        var left = Box(root, "ChevronUpper", new Vector3(3.4f, 0.9f, 0.2f), new Vector3(-0.6f, 1.0f, 0f), mat);
        left.RotationDegrees = new Vector3(0, 0, -30);
        var right = Box(root, "ChevronLower", new Vector3(3.4f, 0.9f, 0.2f), new Vector3(-0.6f, -1.0f, 0f), mat);
        right.RotationDegrees = new Vector3(0, 0, 30);

        return root;
    }

    private static MeshInstance3D Box(Node parent, string name, Vector3 size, Vector3 position, Material mat)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = mat,
        };
        parent.AddChild(node);
        return node;
    }

    /// <summary>
    /// A strut or wire running between two points. Handles the rotation, so the
    /// caller only has to say where it starts and where it ends, which is how you
    /// actually think about rigging.
    /// </summary>
    private static void Strut(Node parent, string name, Vector3 from, Vector3 to,
                              Material mat, float thickness = 0.075f)
    {
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 1e-4f) return;

        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = new Vector3(length, thickness, thickness) },
            Position = (from + to) * 0.5f,
            MaterialOverride = mat,
        };

        // Point the box's long axis down the run.
        Vector3 along = delta / length;
        Vector3 reference = Mathf.Abs(along.Dot(Vector3.Up)) > 0.95f ? Vector3.Back : Vector3.Up;
        Vector3 side = reference.Cross(along).Normalized();
        node.Basis = new Basis(along, side.Cross(along), side);

        parent.AddChild(node);
    }

    /// <summary>A tapering section, for the rear fuselage running back to the sternpost.</summary>
    private static void Taper(Node parent, string name, float frontSize, float backSize,
                              float length, Vector3 center, Material mat)
    {
        const int steps = 4;
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 0.5f) / steps;
            float size = Mathf.Lerp(frontSize, backSize, t);
            float x = center.X + length * (0.5f - t);

            parent.AddChild(new MeshInstance3D
            {
                Name = $"{name}{i}",
                Mesh = new BoxMesh { Size = new Vector3(length / steps + 0.02f, size, size * 0.9f) },
                Position = new Vector3(x, center.Y + (frontSize - size) * 0.12f, center.Z),
                MaterialOverride = mat,
            });
        }
    }

    /// <summary>The engine cowl: a drum lying on its side, facing forward.</summary>
    private static void Drum(Node parent, string name, float radius, float length,
                             Vector3 position, Material mat)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius * 0.96f,
                Height = length,
                RadialSegments = 14,
            },
            Position = position,
            // A cylinder stands up the Y axis by default. Lay it along the nose.
            RotationDegrees = new Vector3(0, 0, 90),
            MaterialOverride = mat,
        });
    }

    private static void Wheel(Node parent, string name, Vector3 position, Material mat)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh { TopRadius = 0.33f, BottomRadius = 0.33f, Height = 0.16f, RadialSegments = 12 },
            Position = position,
            // A cylinder stands up the Y axis by default. Lay it on its side.
            RotationDegrees = new Vector3(90, 0, 0),
            MaterialOverride = mat,
        };
        parent.AddChild(node);
    }

    private static StandardMaterial3D Solid(Color color, float roughness = 0.85f, float metallic = 0f)
        => new() { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
}
