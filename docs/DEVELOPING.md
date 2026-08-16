# How to run and debug Aerodrome

## The quick way

```powershell
.\run.ps1               # build and play
.\run.ps1 -Editor       # open the Godot editor
.\run.ps1 -Capture .\shots   # scripted demo, screenshots, then quit
dotnet test             # 51 tests, about 100 ms
```

`run.ps1` finds Godot on its own, so it keeps working after an update.

## Why F5 does not work in Visual Studio

`Godot.NET.Sdk` builds the game as a **Library**, not an executable. Godot's own
host process loads that assembly through hostfxr. Visual Studio greys out Start
for library projects, so there is no F5 to press.

This is not something the project can fix. It is how Godot .NET works.

Three ways round it, best first.

### 1. VS Code (recommended, and it is already set up)

VS Code has real Godot C# debugging and you have it installed. Open the repo
folder, install the **C#** extension (`ms-dotnettools.csharp`), and press F5.

`.vscode/launch.json` has four configurations:

| Configuration | What it does |
|---|---|
| Play | Builds, then launches the game with the debugger attached |
| Play (scripted demo + screenshots) | The capture routine, writing to `shots/` |
| Godot editor | Opens the editor with the debugger attached |
| Attach to a running Godot | Attaches to a game you started another way |

Breakpoints hit in both `Aerodrome.Core` and the game scripts.

### 2. Visual Studio, through Attach to Process

This works today with no setup and no extension.

1. Start the game: `.\run.ps1`
2. In Visual Studio: **Debug > Attach to Process**
3. Filter for `Godot`
4. Pick `Godot_v4.7.1-stable_mono_win64.exe`
5. Set **Attach to** to `Managed (.NET Core, .NET 5+) code`, then attach

Breakpoints, locals, and the watch window all work from there. The only thing you
lose against F5 is the keystroke.

`game/Properties/launchSettings.json` holds Play and Editor profiles in case a
future Visual Studio starts honouring them for library projects. Try F5 first. If
Start is greyed out, that is the library problem, and Attach is the answer.

### 3. Rider

JetBrains Rider has a first-class Godot plugin: F5, breakpoints, and hot reload,
with no setup beyond pointing it at the Godot executable. If you end up living in
this project, it is the smoothest of the three. It is also the only paid option.

## The winget shim trap

Godot works out where its `GodotSharp/` folder is from its own executable path.
winget installs a shim at `%LOCALAPPDATA%\Microsoft\WinGet\Links\godot.exe`, and
the real engine lives somewhere else entirely.

Launch through the shim and Godot looks for `GodotSharp/` next to the shim, does
not find it, and dies:

```
ERROR: .NET: Assemblies not found
CrashHandlerException: Program crashed with signal 11
```

Always launch the real executable:

```
%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_.../Godot_v4.7.1-stable_mono_win64.exe
```

`run.ps1` and every launch configuration in this repo already do that. This note
is here for the next time you type `godot` by hand and it explodes.

## Build layout

`Godot.NET.Sdk` puts build output inside the Godot project, not in a `bin/` folder
next to the source:

```
game/.godot/mono/temp/bin/Debug/Aerodrome.Game.dll
```

That path is gitignored. If Godot reports missing assemblies after a clean, run
`dotnet build game/Aerodrome.Game.csproj` and try again.

## Controls

There are two ways to fly, and they are genuinely different games. Press `F2` to
switch, or just move a controller's right stick and it switches itself.

### Point to aim (mouse and keyboard)

The cursor's direction from the screen center is where the nose should go, and
the nose slews there. Push the cursor past about two thirds of the way out and it
stops meaning "point here" and starts meaning "keep pulling", so you can hold a
loop instead of having to swirl the mouse in a circle.

### The column (controller)

A real control stick. Back on the right stick pulls toward the canopy, so
inverted, back stick points you at the ground. Shove the left stick hard left or
right and the aircraft swaps ends with a flat turn. It has to return to centre
before it will fire again, so it is a deliberate whack, not something you hold.

| Action | Mouse and keyboard | Controller | Classic (press `C`) |
|---|---|---|---|
| Pitch | Move the mouse | Right stick up/down | Numpad 8-way |
| Flat turn (swap ends) | `A` or middle mouse | Whack left stick left or right, or `A` | The arrow opposite your facing |
| Throttle | `W` / `S` | Triggers | `+` / `-` |
| Guns | Left mouse | Right shoulder | `Space` |
| Roll upright | Right mouse | B | `Ins` |
| Aileron roll | `Q` | X | |
| Near / Far view | `V` | Left shoulder | `V` |
| Opponent skill | `1` / `2` / `3` | | |
| Restart | `R` | | `R` |
| Switch control scheme | `F2` | | `F2` |
| Frame graph | `F3` | | `F3` |

Guns fall off hard past about 90 m and are nearly useless by 320 m, so a long
burst on the merge is wasted ammunition and gun heat. Get close.
