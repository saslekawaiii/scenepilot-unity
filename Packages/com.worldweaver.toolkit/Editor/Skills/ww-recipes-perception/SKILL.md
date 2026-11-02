---
name: ww-recipes-perception
description: Unity API/C# рецепты: Use when users want to get a summary, overview, dependency report, or export of the current scene state.
category: unity-recipes
keywords: perception perception Unity API/C# рецепты: Use when users want to get a summary, overview, dependency report, or export of the current scene state.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/perception/SKILL.md
---
# Unity API reference: perception

Find the relevant C# reference with find_unity_recipes, query "perception <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `perception_analyze`, `perception_scan`, and `perception_describe` do not exist
- `scene_context` is not `editor_get_context`: it exports hierarchy/components/references, while editor context focuses on current editor state
- `scene_analyze`, `scene_health_check`, `scene_contract_validate`, `scene_component_stats`, `scene_find_hotspots`, and `project_stack_detect` belong to this module even if the prefix looks like `scene_*` or `project_*`

**Routing**:
- Current selection/play-mode/editor state -> `editor_get_context`
- Object search by name/path -> `scene_find_objects` or `gameobject_find`
- Script dependency closure -> `script_dependency_graph`

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
