---
name: ww-recipes-cleaner
description: Unity API/C# рецепты: Use when users want to find unused assets, duplicate files, or clean up the project.
category: unity-recipes
keywords: cleaner cleaner Unity API/C# рецепты: Use when users want to find unused assets, duplicate files, or clean up the project.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/cleaner/SKILL.md
---
# Unity API reference: cleaner

Find the relevant C# reference with find_unity_recipes, query "cleaner <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `cleaner_delete` / `cleaner_remove` do not exist → use `asset_delete` / `asset_delete_batch` to actually remove.
- `cleaner_fix` does not exist → use `cleaner_fix_missing_scripts` specifically for missing script references.
- `cleaner_scan` / `cleaner_find_unused` do not exist → use specific skills: `cleaner_find_unused_assets`, `cleaner_find_duplicates`, `cleaner_find_missing_references`, `cleaner_find_empty_folders`, `cleaner_find_large_assets`.

**Routing**:
- To delete found assets → `asset` module's `asset_delete` / `asset_delete_batch`.
- For project validation → `validation` module.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
