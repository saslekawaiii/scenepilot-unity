# Built-in animated characters

Two selected, unmodified FBX source models from **Mini Characters 1 (1.0)** by Kenney. The author distributes the pack under CC0 1.0. The original `License.txt` is preserved verbatim, with exact source/archive/file hashes in `PROVENANCE.json`.

- `adventurer.fbx`: source `character-male-a.fbx`.
- `explorer.fbx`: source `character-female-b.fbx`.
- `colormap.png`: original shared color texture.

These are stylized human character meshes with a seven-bone generic rig and authored animations. They are **not** geometric primitives, a photorealistic character generator, or a Unity Humanoid retargeting rig. The tool creates a movement blend tree from the real `idle`, `walk` and `sprint` clips and uses the real `jump` and `fall` clips for airborne movement. The remaining authored clips stay available in the imported FBX.

The generated scene references these package assets directly; no network, model account or download is required at creation or play time. Generated materials and Animator Controllers are created in the caller’s transaction folder under `Assets/WorldWeaverGenerated`, with the imported texture assigned for Built-in or URP.

Primary sources:

- [Author’s asset page](https://kenney.nl/assets/mini-characters)
- [Author’s upload on OpenGameArt](https://opengameart.org/node/165679)
- [Exact original archive](https://opengameart.org/sites/default/files/kenney_mini-characters.zip)
- [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
