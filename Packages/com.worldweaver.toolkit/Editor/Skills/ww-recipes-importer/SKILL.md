---
name: ww-recipes-importer
description: Unity API/C# рецепты: Use when users want to configure texture, audio, or model import settings.
category: unity-recipes
keywords: importer importer Unity API/C# рецепты: Use when users want to configure texture, audio, or model import settings.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/importer/SKILL.md
---
# Unity API reference: importer

Find the relevant C# reference with find_unity_recipes, query "importer <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `importer_import` does not exist → use `asset_import` in the `asset` module to bring files into the project
- `importer_set_format` does not exist → use the specific texture/audio/model setters
- `importer_get_settings` does not exist → use the category-specific getters
- Settings changes do not always apply instantly in memory. Reimport may still be required

**Routing**:
- File import or refresh → `asset`
- Texture settings → `texture_*`
- Audio settings → `audio_*`
- Model settings → `model_*`
- Alternative importer bridge skills → `texture_set_import_settings`, `audio_set_import_settings`, `model_set_import_settings`
- Force importer refresh → `asset_reimport` or `asset_reimport_batch`

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
