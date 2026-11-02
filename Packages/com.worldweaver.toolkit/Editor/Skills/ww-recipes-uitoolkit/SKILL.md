---
name: ww-recipes-uitoolkit
description: Unity API/C# рецепты: Use when working with UI Toolkit (UITK) for Unity — create/edit USS stylesheets and UXML layouts, configure UIDocument in scenes.
category: unity-recipes
keywords: uitoolkit uitoolkit Unity API/C# рецепты: Use when working with UI Toolkit (UITK) for Unity — create/edit USS stylesheets and UXML layouts, configure UIDocument in scenes.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/uitoolkit/SKILL.md
---
# Unity API reference: uitoolkit

Find the relevant C# reference with find_unity_recipes, query "uitoolkit <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- Requires Unity 2022.3+ — no package install needed; built into the engine
- `uitoolkit_create_button` / `uitoolkit_create_label` do not exist -> use `uitk_add_element`
- `uitoolkit_set_style` does not exist -> use `uitk_add_uss_rule`, `uitk_remove_uss_rule`, or `uitk_modify_element`
- `uitoolkit_create_canvas` does not exist -> UI Toolkit uses `UIDocument`, not Canvas
- `uitk_*` and `ui_*` are different systems. Do not mix UI Toolkit structure/styling assumptions into UGUI workflows
- USS is **not full CSS**. `display:grid`, `box-shadow`, `calc()`, `@media`, `::before`, `z-index`, and gradients are unsupported

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
