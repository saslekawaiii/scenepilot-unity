---
name: ww-recipes-gameobject
description: Unity API/C# рецепты: Use when users want to create, delete, move, rotate, scale, or parent GameObjects.
category: unity-recipes
keywords: gameobject gameobject Unity API/C# рецепты: Use when users want to create, delete, move, rotate, scale, or parent GameObjects.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/gameobject/SKILL.md
---
# Unity API reference: gameobject

Find the relevant C# reference with find_unity_recipes, query "gameobject <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `gameobject_move` / `gameobject_rotate` / `gameobject_set_scale` do not exist → use `gameobject_set_transform` (handles position, rotation, and scale together)
- `gameobject_set_position` does not exist → use `gameobject_set_transform` with `posX/posY/posZ`
- `gameobject_add_component` does not exist → use `component_add` (component module)
- `gameobject_get_transform` does not exist → use `gameobject_get_info` (returns position/rotation/scale)

**Routing**:
- To add/remove components → use `component` module
- To set material/color → use `material` module
- To search objects by name/tag/component → `gameobject_find` (this module) or `scene_find_objects` (scene module, Semi-Auto)

> **Object Targeting**: All single-object skills accept three identifiers: `name` (string), `instanceId` (int, preferred for precision), `path` (string, hierarchy path like "Parent/Child"). Provide at least one. When only `name` is shown in a parameter table, `instanceId` and `path` are also accepted.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
