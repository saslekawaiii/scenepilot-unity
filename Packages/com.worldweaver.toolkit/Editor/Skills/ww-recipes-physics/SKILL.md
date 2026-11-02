---
name: ww-recipes-physics
description: Unity API/C# рецепты: Use when users want to perform raycasts, overlap checks, or configure gravity.
category: unity-recipes
keywords: physics physics Unity API/C# рецепты: Use when users want to perform raycasts, overlap checks, or configure gravity.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/physics/SKILL.md
---
# Unity API reference: physics

Find the relevant C# reference with find_unity_recipes, query "physics <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `physics_add_rigidbody` / `physics_add_collider` do not exist → use `component_add` with componentType "Rigidbody"/"BoxCollider"/etc.
- `physics_simulate` does not exist → physics simulation runs during Play mode
- Raycast results use world-space coordinates

**Routing**:
- For adding physics components → use `component` module
- For physics material → use `physics_create_material` (this module)
- For layer collision matrix → `physics_set_layer_collision` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
