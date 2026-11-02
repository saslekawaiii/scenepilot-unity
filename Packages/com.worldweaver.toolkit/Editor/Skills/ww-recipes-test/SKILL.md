---
name: ww-recipes-test
description: Unity API/C# рецепты: Use when users want to run Unity tests or read test results.
category: unity-recipes
keywords: test test Unity API/C# рецепты: Use when users want to run Unity tests or read test results.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/test/SKILL.md
---
# Unity API reference: test

Find the relevant C# reference with find_unity_recipes, query "test <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `test_run_all` does not exist → use `test_run` or `test_run_by_name`.
- `test_create_template` does not exist → use `test_create_editmode` or
  `test_create_playmode`.
- `test_get_status` does not exist → use `test_get_result` (reads the XML,
  stateless).
- There is no `jobId` anywhere. If older docs mention one, ignore them.
- There is no `test_cancel` — Unity `TestRunnerApi` has no public hard-cancel
  surface.
- There is no `test_smoke_skills` — it depended on an upstream REST skill
  registry that isn't in this pack.

**Routing**:
- For compile error checking → `editor_get_state` (`isCompiling` field).
- For test script creation → `test_create_editmode` / `test_create_playmode`,
  then edit via the `script` module.

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
