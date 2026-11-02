---
name: ww-recipes-project
description: Unity API/C# рецепты: Use when users want to get project settings, quality settings, or shader lists.
category: unity-recipes
keywords: project project Unity API/C# рецепты: Use when users want to get project settings, quality settings, or shader lists.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/project/SKILL.md
---
# Unity API reference: project

Find the relevant C# reference with find_unity_recipes, query "project <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `project_save` does not exist → use `scene_save` (scene module) or `editor_execute_menu` menuPath="File/Save"
- `project_settings` does not exist → use specific skills: `project_get_render_pipeline`, `project_get_build_settings`, etc.
- `project_set_resolution` / `project_set_player_settings` do not exist → Player Settings are read-only via `project_get_player_settings`; to edit, open Project Settings via `editor_execute_menu` with `Edit/Project Settings...`
- `project_create` does not exist → projects are created via Unity Hub, not REST API

**Routing**:
- For Layer/Tag management → `project_add_tag` (this module); Layers are read-only via `project_get_layers` (edit via `editor_execute_menu` → `Edit/Project Settings...`)
- For build settings → `project_get_build_settings` (read-only; use `editor_execute_menu` → `File/Build Settings...` to edit)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
