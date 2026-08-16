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

    public static Node3D Build(Color teamColor, out Node3D propeller)
    {
        var root = new Node3D { Name = "Airframe" };

        var khaki = Solid(Khaki);
        var linen = Solid(Linen);
        var cowl = Solid(Cowl, roughness: 0.45f, metallic: 0.6f);
        var dark = Solid(Dark);
        var wood = Solid(Wood);
        var rubber = Solid(Rubber);
        var team = Solid(teamColor);

        // Fuselage and engine cowl.
        Box(root, "Fuselage", new Vector3(4.6f, 0.80f, 0.72f), new Vector3(-0.20f, 0f, 0f), khaki);
        Box(root, "Cowl", new Vector3(0.90f, 0.86f, 0.82f), new Vector3(2.35f, 0.05f, 0f), cowl);

        // Wings. Upper above, lower below: the clearest read on which way is up.
        Box(root, "UpperWing", new Vector3(1.45f, 0.10f, 8.53f), new Vector3(0.35f, 1.05f, 0f), khaki);
        Box(root, "LowerWing", new Vector3(1.35f, 0.10f, 7.90f), new Vector3(0.15f, -0.38f, 0f), linen);

        // Struts.
        Box(root, "CabaneL", new Vector3(0.09f, 1.05f, 0.09f), new Vector3(0.35f, 0.52f, 0.34f), dark);
        Box(root, "CabaneR", new Vector3(0.09f, 1.05f, 0.09f), new Vector3(0.35f, 0.52f, -0.34f), dark);
        Box(root, "StrutL", new Vector3(0.08f, 1.43f, 0.08f), new Vector3(0.30f, 0.33f, 2.60f), dark);
        Box(root, "StrutR", new Vector3(0.08f, 1.43f, 0.08f), new Vector3(0.30f, 0.33f, -2.60f), dark);

        // Tail. The fin pointing up is the strongest inversion cue in the silhouette.
        Box(root, "Tailplane", new Vector3(0.85f, 0.08f, 2.70f), new Vector3(-2.85f, 0.10f, 0f), linen);
        Box(root, "Fin", new Vector3(0.75f, 0.85f, 0.08f), new Vector3(-2.95f, 0.55f, 0f), khaki);
        Box(root, "FinStripe", new Vector3(0.30f, 0.60f, 0.10f), new Vector3(-3.05f, 0.55f, 0f), team);

        // Cockpit.
        Box(root, "Cockpit", new Vector3(0.58f, 0.32f, 0.58f), new Vector3(0.55f, 0.46f, 0f), dark);
        Box(root, "Headrest", new Vector3(0.26f, 0.24f, 0.46f), new Vector3(0.20f, 0.54f, 0f), khaki);

        // Undercarriage. Wheels below are the other half of the inversion read.
        Box(root, "GearL", new Vector3(0.08f, 0.70f, 0.08f), new Vector3(0.85f, -0.72f, 0.42f), dark);
        Box(root, "GearR", new Vector3(0.08f, 0.70f, 0.08f), new Vector3(0.85f, -0.72f, -0.42f), dark);
        Box(root, "Axle", new Vector3(0.10f, 0.08f, 1.30f), new Vector3(0.85f, -1.05f, 0f), dark);
        Wheel(root, "WheelL", new Vector3(0.85f, -1.05f, 0.62f), rubber);
        Wheel(root, "WheelR", new Vector3(0.85f, -1.05f, -0.62f), rubber);

        // Propeller. Its own node so it can spin about the long axis.
        propeller = new Node3D { Name = "Propeller", Position = new Vector3(2.88f, 0.05f, 0f) };
        root.AddChild(propeller);
        Box(propeller, "Blade", new Vector3(0.07f, 2.55f, 0.20f), Vector3.Zero, wood);
        Box(propeller, "Spinner", new Vector3(0.22f, 0.26f, 0.26f), new Vector3(0.10f, 0f, 0f), cowl);

        return root;
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
