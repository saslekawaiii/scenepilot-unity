---
name: ww-recipes-tooling
description: Unity API/C# рецепты: Use when about to write or install a custom Editor script, add a [MenuItem], build a custom Editor window, or create a one-off Editor helper — to check whether a ready-to-paste template already exists in tooling/(domain)/ before reaching for Unity_CreateScript.
category: unity-recipes
keywords: tooling tooling Unity API/C# рецепты: Use when about to write or install a custom Editor script, add a [MenuItem], build a custom Editor window, or create a one-off Editor helper — to check whether a ready-to-paste template already exists in tooling/(domain)/ before reaching for Unity_CreateScript.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/tooling/SKILL.md
---
# Unity API reference: tooling

Find the relevant C# reference with find_unity_recipes, query "tooling <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
- Pasting a `tooling/` C# block into `Unity_RunCommand`. These are install-once Editor scripts, not `IRunCommand` bodies — they will not compile in the RunCommand context. Always install via `Unity_CreateScript` at the path the tool file specifies.
- Calling `EditorApplication.ExecuteMenuItem` immediately after install. Unity needs to recompile the new script before the menu items register. Poll `isCompiling` first.
- Treating `tooling/` files as compile/run-gated like `recipes/`. They have no validation gate — install into a scratch project once before trusting at scale.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
