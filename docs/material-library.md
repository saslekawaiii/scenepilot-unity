# Materials, textures and asset sources

Unity Editor Agent can texture existing geometry with an offline starter library or supported Poly Haven PBR assets. Material assignment and model replacement are separate workflows: a better surface does not change the object's shape.

## Start offline

The package includes **13 original CC0 starter surfaces**: grass, stone, plaster, wood, roof tile, metal, sand, snow, bark, cloth, leather, water and cobblestone. Their albedo, normal and metallic/smoothness maps are bundled with runtime assets, so they do not need a network download.

Ask in chat:

> Apply the built-in wood material to the selected door, without internet access. Preserve the door's shape and controller. Show which renderers changed.

For a direct picker, the current 1.8.1 menu is **Tools → WorldWeaver → Материалы и текстуры**. Select a Renderer or Terrain in the Hierarchy, choose a surface and adjust tiling. Enable child renderers when applying to a building root that has no Renderer itself.

## Find an online surface

> Find weathered wooden boards on Poly Haven, import a matching material and apply it to the selected door. If the download fails, tell me which fallback was actually used.

The agent follows three separate steps:

| Tool | Purpose |
| --- | --- |
| `material_search` | Return real IDs, names, sources and licenses. Choose `builtin`, `polyhaven` or `any`. |
| `material_import` | Import an exact returned ID. An uncached online material requires `allow_online=true`. |
| `material_apply` | Apply the actual imported ID to an existing Renderer/Terrain; this step does not fetch from the network. |

Use concrete surface terms such as `weathered wood`, `wood planks` or `rough stone`. Check the returned name and tags before applying; a tree-related result can be bark or forest litter rather than door boards.

If an online import fails, the library can return a built-in fallback. Its **actual `MaterialId` and `Warning`** identify the result. A successful fallback is not evidence that the requested online maps were downloaded.

## Where changes go

Imports and provenance receipts are stored under `Assets/WorldWeaverGenerated/Materials/`. Reuse checks recorded hashes. Changed imported assets are preserved rather than silently overwritten.

Application creates a material copy instead of recoloring a shared source asset. Terrain edits clone TerrainData/layer data and preserve other layers and blend weights. The chosen terrain layer must already exist, except index zero on an empty Terrain. Undo restores the previous assignment.

## Render-pipeline support

| Pipeline | Library behavior |
| --- | --- |
| Built-in | Standard material with supported maps. |
| URP | URP Lit material with supported maps. |
| HDRP or another custom pipeline | Requires a compatible shader/material adapter; the built-in importer does not provide one. |

Albedo uses sRGB; data maps use linear sampling. Roughness is converted to smoothness for the supported material layouts. TerrainLayer uses supported color/normal fields; this is not a full HDRP mask workflow. The water surface is a static texture treatment, not a fluid simulation.

If a result renders pink, inspect the active pipeline and shader before adjusting textures. If it is stretched, inspect UVs and tiling. Inspect both the material assignment and the rendered image; a completed import alone does not establish visual quality.

## Resource boundaries and licenses

Poly Haven imports use supported official HTTPS endpoints and **1k** image maps, with bounds of **8 MiB per map** and **24 MiB per material**. The client validates received image data and records hashes. It does not import arbitrary archives, scripts or `.blend` files through this material workflow.

Poly Haven's [asset license](https://polyhaven.com/license) is CC0, and its [API page](https://polyhaven.com/our-api) describes current access and source-credit requirements. The integration displays **Powered by Poly Haven**. Bundled procedural maps have their own [CC0 notice](../Packages/com.worldweaver.toolkit/Runtime/Materials/LICENSE.txt) and [provenance manifest](../Packages/com.worldweaver.toolkit/Runtime/Materials/builtin-provenance.json).

For existing geometry, ask the agent to search project prefabs first. Supported character models have a separate `model_search` → `model_import` → `replace_model` path; a rig still needs compatible animation and control setup. General data acquisition is covered in [web research](web-agent.md). Check each third-party model's own license before redistribution.

Next: [examples](examples.md), [chat workflow](chat-workflow.md), [troubleshooting](troubleshooting.md).

Implementation: [material library](../Packages/com.worldweaver.toolkit/Editor/Materials/WorldMaterialLibrary.cs), [agent tools](../Packages/com.worldweaver.toolkit/Editor/Assistant/WorldMaterialTools.cs), [Poly Haven client](../Packages/com.worldweaver.toolkit/Editor/Materials/PolyHavenMaterialClient.cs).
