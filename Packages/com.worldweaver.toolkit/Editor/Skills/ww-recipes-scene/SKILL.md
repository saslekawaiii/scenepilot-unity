---
name: ww-recipes-scene
description: Unity API/C# рецепты: Use when users want to create, load, save scenes, or get scene hierarchy.
category: unity-recipes
keywords: scene scene Unity API/C# рецепты: Use when users want to create, load, save scenes, or get scene hierarchy.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/scene/SKILL.md
---
# Unity API reference: scene

Find the relevant C# reference with find_unity_recipes, query "scene <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `scene_delete` / `scene_rename` do not exist → delete scene files via `asset_delete`, rename via `asset_move`
- `scene_list` does not exist → use `scene_get_loaded` (loaded scenes) or `asset_find` with `t:Scene` (all scene assets)
- `scene_find_objects` is a simple name/tag/component filter; for regex/layer/path search use `gameobject_find` (Full-Auto)

**Routing**:
- For detailed hierarchy tree → use `perception` module's `hierarchy_describe`
- For scene statistics → use `perception` module's `scene_summarize`
- For screenshot → `scene_screenshot` (this module) uses the Scene View; for a Game Camera capture use the native `Unity_Camera_Capture` tool

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
