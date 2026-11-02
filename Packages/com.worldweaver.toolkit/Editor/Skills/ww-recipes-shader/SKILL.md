---
name: ww-recipes-shader
description: Unity API/C# рецепты: Use when users want to create or inspect shaders.
category: unity-recipes
keywords: shader shader Unity API/C# рецепты: Use when users want to create or inspect shaders.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/shader/SKILL.md
---
# Unity API reference: shader

Find the relevant C# reference with find_unity_recipes, query "shader <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `shader_set_property` does not exist → use `material_set_float`/`material_set_color`/etc. on the material, not the shader
- `shader_apply` / `shader_assign` do not exist → use `material_set_shader` to change a material's shader
- `shader_get_properties` returns shader **property definitions** (name/type/range), not current values → for material instance values use `material_get_properties`
- Shader names are case-sensitive and path-like: `"Standard"`, `"Universal Render Pipeline/Lit"`, not `"standard"` or `"URP Lit"`

**Routing**:
- For material property changes → use `material` module
- For shader keyword control → `material_set_keyword` (material module)
- For global shader keywords → `shader_set_global_keyword` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
