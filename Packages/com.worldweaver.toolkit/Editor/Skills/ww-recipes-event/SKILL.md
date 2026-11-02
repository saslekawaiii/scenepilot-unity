---
name: ww-recipes-event
description: Unity API/C# рецепты: Use when users want to inspect or modify UI events like Button.onClick.
category: unity-recipes
keywords: event event Unity API/C# рецепты: Use when users want to inspect or modify UI events like Button.onClick.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/event/SKILL.md
---
# Unity API reference: event

Find the relevant C# reference with find_unity_recipes, query "event <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `event_create` / `event_trigger` do not exist → UnityEvents are triggered at runtime, not from editor skills
- `event_subscribe` does not exist → use `event_add_listener`
- `event_remove` does not exist → use `event_remove_listener`
- `event_add_listener` requires exact component type and method name on the target

**Routing**:
- For XR interaction events → use `xr` module's `xr_add_interaction_event`
- For C# event code → write via `script` module

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
