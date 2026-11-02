---
name: ww-recipes-probuilder
description: Unity API/C# рецепты: Use when users want to create ProBuilder shapes, extrude faces, bevel edges, subdivide meshes, or perform procedural mesh operations. Requires com.unity.probuilder package.
category: unity-recipes
keywords: probuilder probuilder Unity API/C# рецепты: Use when users want to create ProBuilder shapes, extrude faces, bevel edges, subdivide meshes, or perform procedural mesh operations. Requires com.unity.probuilder package.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/probuilder/SKILL.md
---
# Unity API reference: probuilder

Find the relevant C# reference with find_unity_recipes, query "probuilder <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `probuilder_create_mesh` does not exist -> use `probuilder_create_shape`
- `probuilder_edit_face` does not exist -> use the specific face skills such as `probuilder_extrude_faces`, `probuilder_delete_faces`, `probuilder_merge_faces`
- `probuilder_set_material` and `probuilder_set_face_material` are different -> whole object vs selected faces
- Regular meshes do not become ProBuilder meshes automatically
- Mesh rebuild calls (`ToMesh()` + `Refresh()`) are already handled by the skills. Do not invent a manual rebuild step

**Routing**:
- For ordinary primitive objects without editable topology -> use `gameobject_create`
- For material asset creation or shader work -> use `material`
- For large blockout generation -> combine this module with `material` and `light`, but keep geometry creation here

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
