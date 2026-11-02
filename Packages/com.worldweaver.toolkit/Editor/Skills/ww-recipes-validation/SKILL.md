---
name: ww-recipes-validation
description: Unity API/C# рецепты: Use when users want to find missing scripts, validate references, or check project health.
category: unity-recipes
keywords: validation validation Unity API/C# рецепты: Use when users want to find missing scripts, validate references, or check project health.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/validation/SKILL.md
---
# Unity API reference: validation

Find the relevant C# reference with find_unity_recipes, query "validation <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- Validation skill routes use the `validate_*` prefix, not `validation_*`
- `validation_run` / `validation_check` do not exist → use specific skills such as `validate_scene`, `validate_project_structure`, `validate_missing_references`
- `validation_fix` does not exist → validation skills report issues; use other modules to fix them
- `validation_clean` does not exist → use `cleaner` module for cleanup operations

**Routing**:
- For unused/duplicate asset cleanup → use `cleaner` module
- For missing script fix → `cleaner_fix_missing_scripts` (cleaner module)
- For compile errors → `script_get_compile_feedback` (script module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
