---
name: ww-recipes-console
description: Unity API/C# рецепты: Use when users want to capture, filter, or clear console logs.
category: unity-recipes
keywords: console console Unity API/C# рецепты: Use when users want to capture, filter, or clear console logs.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/console/SKILL.md
---
# Unity API reference: console

Find the relevant C# reference with find_unity_recipes, query "console <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `console_write` does not exist → use `console_log`
- For reading, filtering, or clearing console logs, use the native `Unity_GetConsoleLogs` or `Unity_ReadConsole` — not a custom recipe.

**Routing**:
- Read console entries / stack traces / errors → `Unity_GetConsoleLogs` or `Unity_ReadConsole` (native MCP)
- Clear the console → `Unity_ReadConsole` (clear action)
- Compilation-status check → `editor_get_state` (`isCompiling` field)
- Console settings (collapse, clear-on-play) → `console_set_collapse` / `console_set_clear_on_play` (this module)
- Scripting defines or forced recompile → `debug_get_defines`, `debug_set_defines`, `debug_force_recompile` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
