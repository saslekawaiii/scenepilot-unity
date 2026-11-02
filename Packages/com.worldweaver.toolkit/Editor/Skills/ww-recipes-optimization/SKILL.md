---
name: ww-recipes-optimization
description: Unity API/C# рецепты: Use when users want to optimize textures, meshes, or improve performance.
category: unity-recipes
keywords: optimization optimization Unity API/C# рецепты: Use when users want to optimize textures, meshes, or improve performance.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/optimization/SKILL.md
---
# Unity API reference: optimization

Find the relevant C# reference with find_unity_recipes, query "optimization <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `optimize_scene` / `optimization_run` do not exist → use specific skills: `optimize_analyze_scene`, `optimize_find_large_assets`, `optimize_find_duplicate_materials`, etc.
- `optimize_compress` / `optimize_compress_textures` / `optimize_compress_meshes` / `optimize_compress_audio` do not exist → use `optimize_textures` (textures), `optimize_mesh_compression` (meshes), `optimize_audio_compression` (audio)
- `optimize_set_lod` / `optimize_setup_lod` do not exist → use `optimize_set_lod_group`
- Optimization skills are analysis + action tools — always review results before applying batch changes

**Routing**:
- For profiler metrics → use native `Unity_Profiler_*` tools (see `../../mcp-tools.md`)
- For static batching flags → `optimize_set_static_flags` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
