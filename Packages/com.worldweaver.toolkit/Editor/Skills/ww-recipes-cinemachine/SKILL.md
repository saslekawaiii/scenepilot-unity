---
name: ww-recipes-cinemachine
description: Unity API/C# рецепты: Use when users want to create cinematic cameras, set follow/look targets, or configure camera behaviors.
category: unity-recipes
keywords: cinemachine cinemachine Unity API/C# рецепты: Use when users want to create cinematic cameras, set follow/look targets, or configure camera behaviors.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/cinemachine/SKILL.md
---
# Unity API reference: cinemachine

Find the relevant C# reference with find_unity_recipes, query "cinemachine <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `cinemachine_create` does not exist → use `cinemachine_create_vcam` for virtual cameras
- `cinemachine_set_target` / `cinemachine_set_follow` / `cinemachine_set_lookat` do not exist → use `cinemachine_set_targets` (sets both Follow and LookAt in one call)
- `cinemachine_add_brain` does not exist → `CinemachineBrain` is auto-added to Main Camera
- The runtime type is `CinemachineCamera` (CM3), not `CinemachineVirtualCamera` (CM2). Priority reads/writes go through `.Priority.Value`.

**Routing**:
- For basic Game Camera operations → use `camera` module
- For Scene View camera → use `camera` module's `camera_set_transform`/`camera_look_at`
- For camera animation sequences → use `timeline` module with Cinemachine track

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
