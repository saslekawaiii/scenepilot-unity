---
name: ww-recipes-timeline
description: Unity API/C# рецепты: Use when users want to create timelines or add animation/audio tracks.
category: unity-recipes
keywords: timeline timeline Unity API/C# рецепты: Use when users want to create timelines or add animation/audio tracks.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/timeline/SKILL.md
---
# Unity API reference: timeline

Find the relevant C# reference with find_unity_recipes, query "timeline <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- Package not installed → recipes silently fail; install first: `Unity_PackageManager_ExecuteAction` with `packageId=com.unity.timeline`, `action=Add`
- `timeline_create_animation` / `timeline_add_track` do not exist → use the typed track skills: `timeline_add_animation_track`, `timeline_add_audio_track`, `timeline_add_activation_track`, `timeline_add_control_track`, `timeline_add_signal_track`
- `timeline_add_keyframe` does not exist → Timeline uses clips, not direct keyframes; use `timeline_add_clip`
- `timeline_set_duration` sets the Timeline asset duration, not individual clip duration

**Routing**:
- For Animator parameters/states → use `animator` module
- For runtime animation playback → use `editor_play` or write C# via `script` module

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
