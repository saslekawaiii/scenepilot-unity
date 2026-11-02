---
name: ww-recipes-smart
description: Unity API/C# рецепты: Use when users want to find objects by property conditions, arrange objects in grid/circle/line, or auto-wire serialized references.
category: unity-recipes
keywords: smart smart Unity API/C# рецепты: Use when users want to find objects by property conditions, arrange objects in grid/circle/line, or auto-wire serialized references.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/smart/SKILL.md
---
# Unity API reference: smart

Find the relevant C# reference with find_unity_recipes, query "smart <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `smart_create` / `smart_build` do not exist → smart skills are query/layout tools, not creation tools
- `smart_search` / `smart_query` do not exist → use `smart_scene_query` (component property filters) or `smart_scene_query_spatial` (spatial region filters)
- `smart_move` does not exist → use `smart_snap_to_grid` or `smart_align_to_ground`

**Routing**:
- For creating objects → use `gameobject` module
- For simple object search → use `gameobject_find` or `scene_find_objects`
- For complex scene queries (SQL-like) → `smart_scene_query` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
