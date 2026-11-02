---
name: ww-recipes-material
description: Unity API/C# рецепты: Use when users want to create materials, set colors, textures, emission, or shader properties.
category: unity-recipes
keywords: material material Unity API/C# рецепты: Use when users want to create materials, set colors, textures, emission, or shader properties.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/material/SKILL.md
---
# Unity API reference: material

Find the relevant C# reference with find_unity_recipes, query "material <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `material_set_metallic` / `material_set_smoothness` do not exist → use `material_set_float` with `propertyName="_Metallic"` or `"_Glossiness"` (Standard) / `"_Smoothness"` (URP)
- `material_set_color` r/g/b/a range is **0–1**, not 0–255
- `material_set_property` does not exist → use the specific setter: `material_set_float`, `material_set_int`, `material_set_vector`, `material_set_color`
- `material_get_color` does not exist → use `material_get_properties` (returns all properties including colors)

**Routing**:
- For shader changes → `material_set_shader` (this module)
- For texture tiling → `material_set_texture_scale` / `material_set_texture_offset`
- Pipeline-specific property names differ: check Render Pipeline Compatibility table in this doc

> **Object Targeting**: Single-object skills accept `name` (GameObject name) or `path` (material asset path like `Assets/Materials/X.mat`). For asset-based operations, prefer `path`.

Recipe path rule: `../../recipes/material/<command>.md`

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
