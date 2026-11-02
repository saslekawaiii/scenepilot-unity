---
name: ww-recipes-asset
description: Unity API/C# рецепты: Use when users want to import, move, delete, duplicate, or organize project assets.
category: unity-recipes
keywords: asset asset Unity API/C# рецепты: Use when users want to import, move, delete, duplicate, or organize project assets.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/asset/SKILL.md
---
# Unity API reference: asset

Find the relevant C# reference with find_unity_recipes, query "asset <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `asset_create` does not exist → use `asset_create_folder` (folders), `material_create` (materials), `script_create` (scripts)
- `asset_rename` does not exist → use `asset_move` with new path
- `asset_search` does not exist → use `asset_find` with searchFilter syntax (e.g. `t:Texture2D player`)
- `asset_copy` does not exist → use `asset_duplicate`

**Routing**:
- For texture/model/audio import settings → use `importer` module
- For material creation → use `material` module
- For script creation → use `script` module

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
