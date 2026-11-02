---
name: ww-recipes-component
description: Unity API/C# рецепты: Use when users want to add, remove, or configure components like Rigidbody, Collider, AudioSource.
category: unity-recipes
keywords: component component Unity API/C# рецепты: Use when users want to add, remove, or configure components like Rigidbody, Collider, AudioSource.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/component/SKILL.md
---
# Unity API reference: component

Find the relevant C# reference with find_unity_recipes, query "component <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `component_create` / `component_get` do not exist → use `component_add` (add) and `component_get_properties` (read)
- `component_find` does not exist → use `component_list` to list components on an object
- `componentType` is case-sensitive — `Rigidbody` not `rigidbody`, `BoxCollider` not `boxcollider`
- Custom scripts need exact class name; if namespaced, use `Namespace.ClassName`

**Routing**:
- To create a C# component script → use `script` module's `script_create` first, then `component_add`
- To set multiple properties at once → use `component_set_property_batch`
- To enable/disable a component → `component_set_enabled` (not `component_set_property`)

> **Object Targeting**: All single-object skills accept `name` (string), `instanceId` (int, preferred), and `path` (string, hierarchy path). Provide at least one.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
