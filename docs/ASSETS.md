# Asset license ledger

One row per third-party asset. Fill a row in **before** you download the file,
not after.

## Why this file exists

Most free Sketchfab models are CC-BY. CC-BY needs a visible credit. Some models
are CC-BY-NC, and NC blocks any paid release of this game forever. You cannot
tell which is which from the thumbnail, and you cannot undo the mistake once the
model is in a shipped build.

Rules:

1. Read the license on the model page before you download.
2. Add the row here first.
3. Never edit anything in `assets/source/`. Work in `assets/blender/`.
4. The credits screen reads from this file. Build the screen in M1.

## License rules

| License | Can we use it | What we owe |
|---|---|---|
| CC0 | Yes | Nothing. Credit anyway. |
| CC-BY | Yes | A visible credit with the author name and a link |
| CC-BY-SA | Careful | Share-alike can reach into derived assets. Ask first. |
| CC-BY-NC | **No** | Blocks any paid release. Do not download. |
| CC-BY-ND | **No** | We must decimate and re-bake, which is a derivative. |
| Sketchfab Store | Yes, if bought | Keep the receipt. Record the order number. |

## Aircraft

| Asset | Author | Source | License | Verified | Changes |
|---|---|---|---|---|---|
| Sopwith Camel | bradacvojtech | [Sketchfab](https://sketchfab.com/3d-models/sopwith-camel-70ad9a87976e4d4eaeedaa4cd78dc94b) | **CC-BY 4.0** | yes, 2026-08-16 | decimate to 12k, split propeller, turn round, level off 13 degrees |
| Fokker Dr.I | KojfDiscord | [Sketchfab](https://sketchfab.com/3d-models/fokker-dri-rise-of-flights-700ec52cb0744508b0774219979ca2bc) | **CC-BY 4.0** | yes, 2026-08-16 | decimate to 12k, split propeller, rotate span off X |

### The exact commands used

```powershell
.\tools\prepare-model.ps1 .\assets\source\camel\scene.gltf -Name camel `
    -NoseAxis '-X' -Drop 'ground' -Budget 12000 -TextureSize 1024 -Pitch -13

.\tools\prepare-model.ps1 .\assets\source\dr1\scene.gltf -Name dr1 `
    -RotateZ -90 -NoseAxis '+X' -Drop '0' -Budget 12000 -TextureSize 1024 -Length 5.77
```

Both needed something different, which is the point of inspecting first. The
Camel arrived nose-aft and parked nose-high on its undercarriage. The Dr.I
arrived with its wingspan along X, and shipped a one-face object that had to go.

### The Camel license, confirmed

Checked through the Sketchfab v3 API, which returns the license without a login:

```
GET https://api.sketchfab.com/v3/models/70ad9a87976e4d4eaeedaa4cd78dc94b
  license: "CC Attribution" (slug "by")
  "Author must be credited. Commercial use is allowed."
  isDownloadable: true
  faceCount:   95942
  vertexCount: 49586
  animationCount: 0
```

**Commercial use is allowed.** The only obligation is a visible credit to
`bradacvojtech`, which the credits screen covers.

Two things follow from the numbers. It is about twelve times over the 8000
triangle budget, so it has to be decimated. And it has no animations and no
separate propeller object, so the propeller has to be split out by hand or by
the pipeline script before it can spin.

Use `tools/prepare-model.ps1` for both. See "Model preparation" below.

Roster still to source: Fokker Dr.I, SPAD XIII, Albatros D.Va, Nieuport 17,
Fokker D.VII.

### Checking a license before you download

Do not trust the page, which is script-rendered and hard to read. Ask the API:

```powershell
$uid = "70ad9a87976e4d4eaeedaa4cd78dc94b"
(Invoke-RestMethod "https://api.sketchfab.com/v3/models/$uid").license.label
```

`CC Attribution` and `CC0 Public Domain` are fine. Anything with
`Noncommercial` in it is not, and must not be downloaded.

## Audio

| Asset | Author | Source | License | Verified | Changes |
|---|---|---|---|---|---|

## Textures and skies

| Asset | Author | Source | License | Verified | Changes |
|---|---|---|---|---|---|

## Model preparation

`tools/prepare-model.ps1` does all of it in headless Blender. You do not have to
open Blender at all.

### Getting the Camel

Sketchfab needs an account to download, and its download API needs a token, so
this one step has to be done by hand:

1. Sign in to Sketchfab and open the
   [Camel](https://sketchfab.com/3d-models/sopwith-camel-70ad9a87976e4d4eaeedaa4cd78dc94b).
2. Download, choosing **glTF** if it is offered.
3. Unpack it into `assets/source/camel/`. That folder is not in git.

### Converting it

Always inspect first. Downloaded models arrive in whatever orientation and scale
the artist left them in, and the inspection tells you which flags you need:

```powershell
.\tools\prepare-model.ps1 -Inspect .\assets\source\camel\scene.gltf
```

It prints the bounding box, the triangle count, the object list, and a guess at
which axis runs nose to tail. Then convert, passing whatever rotation the
inspection implies. glTF is usually Y-up with the model facing `-Z`:

```powershell
.\tools\prepare-model.ps1 .\assets\source\camel\scene.gltf -Name camel -NoseAxis -Z
```

The script re-orients the nose to `+X`, scales the model to a real 5.71 m, puts
the origin at the center of gravity, splits the propeller into its own object so
it can spin, decimates to the 8000 triangle budget, and writes
`assets/export/camel.glb`.

Godot picks that file up on the next run with no code change. If it is not there,
the game falls back to the procedural box airframe, so a fresh clone still runs.

### Splitting the propeller

The script looks for an object whose name contains `prop`, `blade`, `airscrew`,
`screw`, `spinner` or `vrtule`. If it cannot find one it cuts off everything
ahead of a plane near the nose, which is crude but usually right. It prints which
route it took. If the propeller comes out wrong, adjust `-PropCut`.

### Control surfaces

The imported model has none, so ailerons, elevator and rudder stop deflecting
once a real model is in use. Splitting those out is a later job. The procedural
airframe keeps them, which is a reason to keep it around.
