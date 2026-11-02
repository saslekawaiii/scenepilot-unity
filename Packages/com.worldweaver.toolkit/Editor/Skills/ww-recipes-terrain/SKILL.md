---
name: ww-recipes-terrain
description: Unity API/C# рецепты: Use when users want to create terrain, set heights, paint textures, or add trees.
category: unity-recipes
keywords: terrain terrain Unity API/C# рецепты: Use when users want to create terrain, set heights, paint textures, or add trees.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/terrain/SKILL.md
---
# Unity API reference: terrain

Find the relevant C# reference with find_unity_recipes, query "terrain <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `terrain_set_texture` does not exist → use `terrain_paint_texture` with layer index and brush parameters
- `terrain_add_tree` / `terrain_add_grass` do not exist → these require Unity Terrain tools or custom scripts
- `terrain_set_size` does not exist → terrain dimensions are set at creation via `terrain_create`
- `terrain_import_heightmap` / `terrain_set_heights` do not exist → use `terrain_set_heights_batch` with a 2D heights array (`[z][x]` values 0-1)

**Routing**:
- For terrain material → use `material` module on terrain's material
- For objects on terrain → use `gameobject` module to create/place objects

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
