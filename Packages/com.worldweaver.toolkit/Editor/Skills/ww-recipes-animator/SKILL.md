---
name: ww-recipes-animator
description: Unity API/C# рецепты: Use when users want to create animation controllers, add parameters, play animations, or control animation state machines.
category: unity-recipes
keywords: animator animator Unity API/C# рецепты: Use when users want to create animation controllers, add parameters, play animations, or control animation state machines.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/animator/SKILL.md
---
# Unity API reference: animator

Find the relevant C# reference with find_unity_recipes, query "animator <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `animator_create_clip` / `animator_add_clip` do not exist → AnimationClips are created via Unity Editor or asset import
- `animator_set_speed` does not exist → use `component_set_property` on Animator component with propertyName="speed"

**Routing**:
- For Timeline animation → use `timeline` module
- For component properties on Animator → use `component` module
- For animation import settings → use `importer` module

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
