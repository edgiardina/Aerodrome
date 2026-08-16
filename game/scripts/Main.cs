using System;
using System.Collections.Generic;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Root of the running game. Builds the world in code, then does two jobs:
///
///   _PhysicsProcess - step Aerodrome.Core exactly once, at a fixed 120 Hz.
///   _Process        - render an interpolation between the last two ticks.
///
/// No gameplay logic goes in the render path. Gameplay never reads a frame delta.
/// </summary>
public sealed partial class Main : Node3D
{
    private static readonly Color PlayerColor = new(0.42f, 0.66f, 0.92f);
    private static readonly Color EnemyColor = new(0.86f, 0.30f, 0.24f);

    private SimRunner _sim = null!;
    private PlayerInput _input = null!;
    private ChaseCamera _camera = null!;
    private Hud _hud = null!;
    private Minimap _minimap = null!;
    private readonly List<AircraftView> _views = new();
    private readonly List<DamageEffects> _effects = new();
    private BulletView _bulletView = null!;
    private PilotAi _enemyPilot = null!;
    private AiSkill _skill = AiSkill.Veteran;
    private int _roundNumber;

    // --- Automated capture, for checking the build without a human at the keyboard.
    // Run: godot --path game -- --shot <dir>
    private string? _shotDir;
    private DemoPilot? _autoPilot;
    private double _captureTime;
    private int _nextShot;

    public override void _Ready()
    {
        // The sim tick and the physics tick must be the same number, or the fixed
        // timestep is a lie and the interpolation factor means nothing.
        Engine.PhysicsTicksPerSecond = (int)FlightModel.TickRate;
        Engine.MaxFps = 0;

        InputBindings.Register();
        ParseCaptureArgs();

        BuildWorld();
        StartMatch();
    }

    /// <summary>
    /// Capture mode is driven by an environment variable, not a command-line flag.
    /// Godot and the shell both fight over argv, and neither fights over the env.
    /// </summary>
    private void ParseCaptureArgs()
    {
        GD.Print($"[aerodrome] ready. sim {FlightModel.TickRate:F0} Hz, refresh {DisplayServer.ScreenGetRefreshRate():F0} Hz");

        string dir = OS.GetEnvironment("AERODROME_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        _shotDir = dir.Replace('\\', '/').TrimEnd('/');
        _autoPilot = new DemoPilot();
        GD.Print($"[capture] writing frames to {_shotDir}");
    }

    /// <summary>
    /// Fly the scripted routine, screenshot at fixed times, and quit. Scheduled on
    /// elapsed seconds, not frames, because the game runs at over a thousand frames
    /// a second on this machine and a frame count means nothing.
    /// </summary>
    private static readonly (double At, string Name, bool FarView)[] CaptureSchedule =
    {
        (1.20, "01-level",            false),
        (2.12, "02-flat-turn",        false),
        (6.60, "03-half-loop",        false),
        (11.50, "04-far-view",        true),
        (16.00, "05-dogfight",        false),
        (21.00, "06-dogfight-late",   false),
        (23.80, "07-tracers",         false),
        (28.00, "08-smoking",         false),
        (31.50, "09-burning",         false),
    };

    private void RunCapture(double delta)
    {
        _captureTime += delta;

        // Force damage on so the capture always exercises the smoke and fire trails.
        // Capture mode only: nothing here runs in a normal session.
        var player = _sim.Player.State;
        if (_captureTime is > 26.0 and < 26.2) { player.EngineHealth = 0.25; player.AirframeIntegrity = 0.45; }
        if (_captureTime is > 30.0 and < 30.2) { player.OnFire = true; player.FireTime = 0; }

        if (_nextShot >= CaptureSchedule.Length)
        {
            GetTree().Quit();
            return;
        }

        var (at, name, farView) = CaptureSchedule[_nextShot];

        // Flip the view a moment early so the zoom has settled by the shot.
        if (_camera.FarView != farView && _captureTime >= at - 0.9) _camera.ToggleFarView();
        if (_captureTime < at) return;

        Shot(name);
        _nextShot++;
    }

    private void Shot(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{_shotDir}/{name}.png");

        var s = _sim.Player.State;
        GD.Print($"[capture] {name,-14} t={_captureTime:F2}s phase={_autoPilot?.Phase,-10} " +
                 $"alt={s.Position.Y,4:F0}m spd={s.Airspeed * 3.6,3:F0}km/h " +
                 $"hdg={Angles.ToDegrees(s.Theta),4:F0} inv={(s.IsInverted ? "Y" : "-")} " +
                 $"flat={s.FlatTurnProgress:F2} guns={(s.GunsCanBear ? "ok" : "MASKED")} " +
                 $"view={_camera.VisibleWidthM:F0}m");
    }

    // --- Match setup --------------------------------------------------------

    /// <summary>
    /// Arena size is a design lever, not a constant, and it is set by two numbers
    /// that have nothing to do with taste.
    ///
    /// Turn radius at corner speed is about 33 m, so a 6 km arena would make the
    /// whole dogfight a speck. And Near View is only 200 m tall, so a tall arena
    /// means you spend most of the fight staring at empty sky with no ground to
    /// judge your altitude against. Wide and shallow is the shape that works, and
    /// it is the shape the original used.
    ///
    /// 2600 x 800 gives about seven screens across and four tall.
    /// </summary>
    private static Arena TestArena() => new()
    {
        Name = "Test Range",
        WidthM = 2600,
        CeilingM = 800,
        Wind = new Vec2(-4, 0),
        FleeTimeoutS = 10.0,
    };

    private void StartMatch()
    {
        foreach (var view in _views) view.QueueFree();
        _views.Clear();
        _bulletView?.QueueFree();

        _roundNumber++;
        uint seed = (uint)(_roundNumber * 7919 + 13);

        var arena = TestArena();
        var spec = AircraftSpec.CamelArcade;
        _sim = new SimRunner(arena, seed);

        _sim.Add(SimAircraft.Create(spec, team: 0, "player",
            AircraftState.Spawn(spec, new Vec2(700, 300), 0.0, 62.0)));

        _sim.Add(SimAircraft.Create(spec, team: 1, "red",
            AircraftState.Spawn(spec, new Vec2(1900, 380), Math.PI, 60.0)));

        _enemyPilot = new PilotAi(_skill, seed + 101u);

        foreach (var effect in _effects) effect.QueueFree();
        _effects.Clear();

        foreach (var aircraft in _sim.Aircraft)
        {
            var view = AircraftView.Create(aircraft, aircraft.Team == Team.Player ? PlayerColor : EnemyColor);
            AddChild(view);
            _views.Add(view);

            // Trails live in world space, so they hang off the scene root rather
            // than the aircraft. Parenting them to the model would drag the smoke
            // along with it instead of leaving it behind.
            var effect = DamageEffects.Create();
            AddChild(effect);
            _effects.Add(effect);
        }

        _bulletView = BulletView.Create(_sim.Bullets, playerTeam: 0);
        AddChild(_bulletView);

        _autoPilot?.SetEnemy(_sim.Aircraft[1]);

        if (_camera is not null) { _camera.QueueFree(); _hud.QueueFree(); _minimap.QueueFree(); }

        _camera = ChaseCamera.Create(_sim, arena);
        AddChild(_camera);

        var ui = new CanvasLayer { Name = "UI" };
        AddChild(ui);
        _hud = Hud.Create(_sim, _camera, _input, () => _skill);
        _minimap = Minimap.Create(_sim, _camera);
        ui.AddChild(_hud);
        ui.AddChild(_minimap);
    }

    // --- The two loops ------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _sim.Player.Input = !_sim.Player.State.IsAlive ? AircraftInput.Neutral
            : _autoPilot is not null ? _autoPilot.Fly(_sim.Player, _sim.Arena)
            : _input.Poll();

        for (int i = 1; i < _sim.Aircraft.Count; i++)
        {
            var enemy = _sim.Aircraft[i];
            enemy.Input = _enemyPilot.Fly(enemy.Combatant, _sim.Player.Combatant, _sim.Arena, delta);
        }

        _sim.Step();
    }

    public override void _Process(double delta)
    {
        // The aim code needs to know where the nose is and which way a pull goes,
        // so a held stick can keep the turn coming instead of parking on a heading.
        var player = _sim.Player.State;
        _input.FacingHint = player.Facing;
        _input.NoseHeading = player.Theta;
        _input.CanopySign = player.CanopySign;

        if (_input.ConsumeViewToggle()) _camera.ToggleFarView();
        if (Input.IsActionJustPressed(InputBindings.DebugOverlay)) _hud.ShowDebug = !_hud.ShowDebug;
        if (Input.IsActionJustPressed(InputBindings.Restart)) { StartMatch(); return; }

        if (_autoPilot is null)
        {
            HandleDifficultyKeys();
            if (_sim is null) return;
        }

        // The one number that keeps a scrolling game smooth: render between ticks,
        // never on them.
        double alpha = Engine.GetPhysicsInterpolationFraction();

        _camera.Render(alpha, delta);
        foreach (var view in _views) view.Render(alpha, _camera.VisibleWidthM);
        _bulletView.Render();

        for (int i = 0; i < _effects.Count; i++)
        {
            var rs = _sim.Aircraft[i].Interpolated(alpha);
            _effects[i].Render(_sim.Aircraft[i].State, new Vector3((float)rs.X, (float)rs.Y, 0f));
        }

        if (_shotDir is not null) RunCapture(delta);
    }

    /// <summary>Number keys pick the opponent. Takes effect on the next round.</summary>
    private void HandleDifficultyKeys()
    {
        AiSkill? picked =
            Input.IsKeyPressed(Key.Key1) ? AiSkill.Rookie :
            Input.IsKeyPressed(Key.Key2) ? AiSkill.Veteran :
            Input.IsKeyPressed(Key.Key3) ? AiSkill.Ace : null;

        if (picked is not null && picked != _skill)
        {
            _skill = picked;
            GD.Print($"[aerodrome] opponent set to {_skill.Name}, starting a new round");
            StartMatch();
        }
    }

    // --- World --------------------------------------------------------------

    private void BuildWorld()
    {
        _input = new PlayerInput { Name = "PlayerInput" };
        AddChild(_input);

        AddChild(new WorldEnvironment { Name = "Environment", Environment = BuildEnvironment() });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.15f,
            LightColor = new Color(1.0f, 0.96f, 0.88f),
            ShadowEnabled = true,
        };
        // A directional light shines along its own -Z. The camera also looks along
        // -Z, so a light near zero rotation lights exactly the faces we can see.
        // Pitch it down and swing it aside to get relief instead of a flat wash.
        sun.RotationDegrees = new Vector3(-32, 22, 0);
        AddChild(sun);

        var fill = new DirectionalLight3D
        {
            Name = "SkyFill",
            LightEnergy = 0.28f,
            LightColor = new Color(0.62f, 0.74f, 0.92f),
            ShadowEnabled = false,
        };
        fill.RotationDegrees = new Vector3(52, 140, 0);
        AddChild(fill);

        BuildTerrain(TestArena());
    }

    private static Godot.Environment BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.24f, 0.43f, 0.70f),
            SkyHorizonColor = new Color(0.74f, 0.80f, 0.85f),
            // The sky's lower hemisphere must read as haze, not as ground. Real
            // ground is drawn by the terrain layers, and a brown sky-ground puts a
            // second competing horizon across the middle of the screen.
            GroundHorizonColor = new Color(0.70f, 0.76f, 0.79f),
            GroundBottomColor = new Color(0.56f, 0.62f, 0.62f),
            SunAngleMax = 24f,
        };

        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            // Only a touch of haze. The terrain layers carry their own aerial
            // perspective in their albedo, which is far easier to control. Heavy fog
            // on top of that just turns every ridge into a white slab.
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.70f, 0.80f),
            FogDensity = 0.00008f,
            FogSkyAffect = 0.0f,
        };
    }

    /// <summary>
    /// Ground and terrain are flat cardboard layers standing at different depths,
    /// not a 3D landscape.
    ///
    /// This is forced by the camera. It looks straight along -Z, parallel to the
    /// ground, so a real receding ground plane would put its horizon at the exact
    /// center of the screen no matter where the aircraft flew. Flat cutouts with
    /// their top edge at the right altitude are what a side view actually wants,
    /// and parallax is then free: a layer further out in -Z moves less on screen
    /// as the camera scrolls. None of this touches gameplay.
    /// </summary>
    private void BuildTerrain(Arena arena)
    {
        var root = new Node3D { Name = "Terrain" };
        AddChild(root);

        float width = (float)arena.WidthM;
        var rng = new Random(20260816);

        // The ground: a wall facing the camera with its top edge exactly at y = 0,
        // so "altitude 0" on the HUD is the line you crash into.
        Flat(root, "Ground", new Vector3(width * 6f, 900f, 24f),
             new Vector3(width * 0.5f, -450f, -30f), new Color(0.22f, 0.24f, 0.15f));

        // Four bands of country, each darker and greener the closer it is. The
        // colours do the aerial perspective by hand, which is far easier to control
        // than leaning on fog to wash the distance out.
        // Terrain has to sit well below normal fighting altitude, or the horizon
        // lands at eye level and you lose all sense of how high you are. Keep the
        // hills low: at 250 m they are scenery, and at 80 m they are a threat.
        AddRidgeLayer(root, rng, width, z: -120f, baseHeight: 4f, variance: 11f,
                      color: new Color(0.18f, 0.21f, 0.11f), step: 62f, depth: 16f);

        AddRidgeLayer(root, rng, width, z: -430f, baseHeight: 12f, variance: 22f,
                      color: new Color(0.23f, 0.27f, 0.15f), step: 118f, depth: 20f);

        AddRidgeLayer(root, rng, width, z: -1150f, baseHeight: 26f, variance: 38f,
                      color: new Color(0.30f, 0.34f, 0.23f), step: 215f, depth: 24f);

        AddRidgeLayer(root, rng, width, z: -2600f, baseHeight: 55f, variance: 62f,
                      color: new Color(0.41f, 0.46f, 0.43f), step: 390f, depth: 28f);

        AddCloudLayer(root, rng, width, z: -700f, count: 22,
                      minY: 520f, maxY: 1200f, scale: 1.0f, alpha: 0.92f);
        AddCloudLayer(root, rng, width, z: -2100f, count: 18,
                      minY: 700f, maxY: 1700f, scale: 2.2f, alpha: 0.78f);
    }

    /// <summary>One flat cutout standing at a fixed depth.</summary>
    private static void Flat(Node3D parent, string name, Vector3 size, Vector3 position, Color color)
        => parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1.0f },
        });

    private static void AddRidgeLayer(Node3D parent, Random rng, float arenaWidth,
                                      float z, float baseHeight, float variance,
                                      Color color, float step, float depth)
    {
        var layer = new Node3D { Name = $"Ridge{(int)-z}" };
        parent.AddChild(layer);

        var material = new StandardMaterial3D { AlbedoColor = color, Roughness = 1.0f };
        float span = arenaWidth * 3f;

        for (float x = -span * 0.35f; x < span; x += step)
        {
            float h = baseHeight + (float)rng.NextDouble() * variance;
            // Overlap only slightly, so the layer reads as a broken skyline rather
            // than one long flat-topped wall.
            float w = step * (1.02f + (float)rng.NextDouble() * 0.35f);

            // A cutout whose TOP edge sits exactly at h, skirted far below ground so
            // there is never a gap under it. Thin in Z, so the camera never catches
            // a lit top face and turns the whole ridge into a white slab.
            const float skirt = 900f;
            layer.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, h + skirt, depth) },
                Position = new Vector3(x, (h - skirt) * 0.5f, z),
                MaterialOverride = material,
            });
        }
    }

    private static void AddCloudLayer(Node3D parent, Random rng, float arenaWidth,
                                      float z, int count, float minY, float maxY,
                                      float scale, float alpha)
    {
        var layer = new Node3D { Name = $"Cloud{(int)-z}" };
        parent.AddChild(layer);

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.97f, 0.97f, 0.98f, alpha),
            Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            Roughness = 1.0f,
        };

        for (int i = 0; i < count; i++)
        {
            float cx = (float)rng.NextDouble() * arenaWidth * 2.4f - arenaWidth * 0.7f;
            float cy = minY + (float)rng.NextDouble() * (maxY - minY);

            // A few overlapping lumps read as one cloud.
            int lumps = 3 + rng.Next(3);
            for (int j = 0; j < lumps; j++)
            {
                float w = (70f + (float)rng.NextDouble() * 120f) * scale;
                float h = w * (0.30f + (float)rng.NextDouble() * 0.22f);
                layer.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = w * 0.5f, Height = h, RadialSegments = 10, Rings = 5 },
                    Position = new Vector3(
                        cx + (float)(rng.NextDouble() - 0.5) * w * 1.6f,
                        cy + (float)(rng.NextDouble() - 0.5) * h * 0.9f,
                        z + (float)(rng.NextDouble() - 0.5) * 60f),
                    MaterialOverride = material,
                });
            }
        }
    }
}
