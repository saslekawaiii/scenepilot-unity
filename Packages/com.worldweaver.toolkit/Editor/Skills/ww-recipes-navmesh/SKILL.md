---
name: ww-recipes-navmesh
description: Unity API/C# рецепты: Use when users want to bake NavMesh or calculate paths for AI navigation.
category: unity-recipes
keywords: navmesh navmesh Unity API/C# рецепты: Use when users want to bake NavMesh or calculate paths for AI navigation.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/navmesh/SKILL.md
---
# Unity API reference: navmesh

Find the relevant C# reference with find_unity_recipes, query "navmesh <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `navmesh_create` does not exist → use `navmesh_bake` to generate NavMesh
- `navmesh_add_agent_component` / `navmesh_set_agent_speed` do not exist → use `navmesh_add_agent` + `navmesh_set_agent` (convenience wrappers), or `component_add`/`component_set_property` for full control
- NavMesh must be re-baked after scene geometry changes

**Routing**:
- For NavMeshAgent/NavMeshObstacle components → use `component` module
- For path calculation → `navmesh_calculate_path` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
