---
name: ww-recipes-scriptableobject
description: Unity API/C# рецепты: Use when users want to create, read, or modify ScriptableObject assets.
category: unity-recipes
keywords: scriptableobject scriptableobject Unity API/C# рецепты: Use when users want to create, read, or modify ScriptableObject assets.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/scriptableobject/SKILL.md
---
# Unity API reference: scriptableobject

Find the relevant C# reference with find_unity_recipes, query "scriptableobject <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `scriptableobject_create_type` does not exist → create SO scripts via `script_create` with template "ScriptableObject"
- `scriptableobject_get_properties` / `scriptableobject_read` do not exist → use `scriptableobject_get`
- `scriptableobject_set_property` / `scriptableobject_set_field` do not exist → use `scriptableobject_set` (single field) or `scriptableobject_set_batch` (multiple fields)
- `scriptableobject_save` does not exist → changes are auto-saved to the asset

**Routing**:
- For ScriptableObject script creation → use `script` module with template "ScriptableObject"
- For JSON import/export → `scriptableobject_import_json` / `scriptableobject_export_json` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
