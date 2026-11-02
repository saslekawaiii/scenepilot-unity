---
name: ww-recipes-editor
description: Unity API/C# рецепты: Use when users want to enter play mode, select objects, undo/redo, or execute menu commands.
category: unity-recipes
keywords: editor editor Unity API/C# рецепты: Use when users want to enter play mode, select objects, undo/redo, or execute menu commands.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/editor/SKILL.md
---
# Unity API reference: editor

Find the relevant C# reference with find_unity_recipes, query "editor <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `editor_run` does not exist → use `editor_play` to enter play mode
- `editor_compile` / `editor_recompile` do not exist → use `debug_force_recompile`
- `editor_save` does not exist → use `editor_execute_menu` with menuPath `"File/Save"`
- `editor_execute_menu` requires exact menu path — typos cause silent failure

**Routing**:
- For compilation check → use `editor_get_state` (`isCompiling` field)
- For console errors → use native `Unity_ReadConsole` or `Unity_GetConsoleLogs`
- For scene save → `scene_save` (scene module) or `editor_execute_menu` menuPath="File/Save"

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
