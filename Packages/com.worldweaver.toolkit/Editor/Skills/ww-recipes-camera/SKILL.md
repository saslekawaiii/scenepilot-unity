---
name: ww-recipes-camera
description: Unity API/C# рецепты: Use when users want to move, rotate, or align the editor camera view.
category: unity-recipes
keywords: camera camera Unity API/C# рецепты: Use when users want to move, rotate, or align the editor camera view.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/camera/SKILL.md
---
# Unity API reference: camera

Find the relevant C# reference with find_unity_recipes, query "camera <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `camera_move` / `camera_rotate` do not exist → use `camera_set_transform` (Scene View) or `gameobject_set_transform` (Game Camera)
- `camera_set_fov` does not exist → use `camera_set_properties` with `fieldOfView` parameter
- `camera_*` skills control **two different cameras**: `camera_set_transform`/`camera_look_at`/`camera_align_view_to_object` control the **Scene View camera**; `camera_create`/`camera_set_properties`/`camera_screenshot` control **Game Cameras**
- `camera_delete` does not exist → use `gameobject_delete` on the camera GameObject

**Routing**:
- For Cinemachine virtual cameras → use `cinemachine` module
- For Game Camera component properties → `camera_set_properties` / `camera_get_properties` (this module)
- For scene screenshots → `scene_screenshot` (scene module) uses the Scene View; for in-memory Game Camera inspection use `Unity_Camera_Capture`; for persisted Game Camera PNGs at custom resolution use `camera_screenshot` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
