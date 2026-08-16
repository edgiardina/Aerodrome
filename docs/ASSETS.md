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
| Sopwith Camel | bradacvojtech | [Sketchfab](https://sketchfab.com/3d-models/sopwith-camel-70ad9a87976e4d4eaeedaa4cd78dc94b) | **NOT YET CHECKED** | no | none yet |

The Sopwith Camel above is the model Ed found. The page lists it as a free
download, which usually means CC-BY, but the page is script-rendered and the
license was not readable from a fetch. **Open the page and confirm the license
before you download it.**

Roster still to source: Fokker Dr.I, SPAD XIII, Albatros D.Va, Nieuport 17,
Fokker D.VII.

## Audio

| Asset | Author | Source | License | Verified | Changes |
|---|---|---|---|---|---|

## Textures and skies

| Asset | Author | Source | License | Verified | Changes |
|---|---|---|---|---|---|

## Model preparation steps

Do these in Blender for every aircraft, then export `.glb` to `assets/export/`.

1. Put the origin at the center of gravity.
2. Point the nose along `+X`.
3. Split the propeller into its own object so it can spin.
4. Decimate to 8000 triangles or fewer.
5. Bake the textures to one atlas.
6. Add an LOD1 at 40 percent.
