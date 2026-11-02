---
name: ww-recipes-prefab
description: Unity API/C# рецепты: Use when users want to create, instantiate, apply, or unpack prefabs.
category: unity-recipes
keywords: prefab prefab Unity API/C# рецепты: Use when users want to create, instantiate, apply, or unpack prefabs.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/prefab/SKILL.md
---
# Unity API reference: prefab

Find the relevant C# reference with find_unity_recipes, query "prefab <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `prefab_create_from_object` does not exist → use `prefab_create` (takes scene object name/instanceId and savePath)
- `prefab_spawn` does not exist → use `prefab_instantiate`
- `prefab_edit` / `prefab_modify` do not exist → use `prefab_set_property` (edit prefab asset directly) or instantiate, modify, then `prefab_apply`
- `prefab_save` does not exist → use `prefab_apply` (applies instance changes to source prefab)

**Routing**:
- To modify components on a prefab instance in scene → use `component` module skills, then `prefab_apply`
- To set a property directly on the prefab asset → `prefab_set_property` (this module)
- To find all instances of a prefab → `prefab_find_instances` (this module)

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
