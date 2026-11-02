---
name: ww-recipes-light
description: Unity API/C# рецепты: Use when users want to create or configure lights (Directional, Point, Spot, Area).
category: unity-recipes
keywords: light light Unity API/C# рецепты: Use when users want to create or configure lights (Directional, Point, Spot, Area).
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/light/SKILL.md
---
# Unity API reference: light

Find the relevant C# reference with find_unity_recipes, query "light <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `light_add` does not exist → use `light_create` (creates a new light GameObject)
- `light_set_color` / `light_set_intensity` do not exist → use `light_set_properties` (sets color, intensity, range, shadows together)
- `light_delete` does not exist → use `gameobject_delete` on the light's GameObject
- `light_set_shadow` does not exist → use `light_set_properties` with `shadows` parameter ("none"/"hard"/"soft")

**Routing**:
- For lightmap baking settings → `light_get_lightmap_settings` (this module)
- For reflection probes → `light_add_reflection_probe` (this module)
- For light probe groups → `light_add_probe_group` (this module)

> **Object Targeting**: All single-object skills accept `name` (string) and `instanceId` (int, preferred). Provide at least one. `path` (hierarchy path) is also accepted where noted.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
