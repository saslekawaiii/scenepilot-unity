# Skills and Unity API recipes

Unity Editor Agent ships **114 Markdown skills** and **466 Unity API recipes**. Skills describe an approach and useful checks; recipes provide reference code. The available executable operations still come from the host's actual tool registry and project permissions.

## Use the bundled library

Skill retrieval is enabled by **«Использовать навыки»** in chat settings. You can ask the agent to discover relevant references before implementing an unfamiliar operation:

> Inspect the existing project setup, find the relevant Unity API recipes and adapt them to this version. Reuse the current controller, then compile and check the resulting behavior.

The agent's reference workflow is:

1. `discover_tools` finds the actual available operations.
2. `list_skills` / `read_skill` retrieve a relevant guide.
3. `find_unity_recipes` searches by Unity type, operation or domain.
4. `read_unity_recipe` retrieves the exact returned ID, including additional pages/prerequisites when needed.
5. `reflect_api` checks public signatures in the current project before unfamiliar code is used.
6. An existing tool, reviewed runtime script or permitted `execute_editor_code` applies the operation; subsequent observations check the result.

English API terms such as `HingeJoint`, `Terrain holes` or `Canvas raycaster` often work better than a broad natural-language search. A recipe may depend on a package or API version your project does not have.

## Enable code only when needed

**«Автономный C# с доступом Unity Editor»** allows generated Editor snippets. A snippet is a method body returning an observation; an omitted `target` is `null`, so code must explicitly find or receive an existing target. Leading `using` directives are supported. A saved runtime `MonoBehaviour` instead needs a source file, real Unity compilation and component assignment.

**«Импортировать и подключать игровые скрипты автоматически»** controls autonomous runtime-script import. Without the relevant permission, references remain useful for planning or reviewed drafts. Ask mode does not gain scene-edit permission from a skill.

Editor code runs with Editor access on the main thread. Use it only in trusted projects and inspect generated source. A Markdown instruction cannot make arbitrary code sandboxed, transactional or safe to run forever.

## Add your own Markdown guide

In advanced chat settings, **«Дополнительные папки с навыками · по одной на строку»** accepts up to eight local roots. Use this layout:

```text
MyAgentSkills/
└── studio-door-conventions/
    └── SKILL.md
```

A minimal guide:

```markdown
---
name: studio-door-conventions
description: Project conventions for configuring and checking doors.
category: physics
keywords: door hinge interaction collision
tools: inspect_object inspect_component verify_scene
mode: advisory
---

# Studio door conventions

Inspect the existing hinge axis, input mapping and collision layers.
Reuse the project's controller. Verify closed collision and opened passage.
```

The folder name must match `name`: lowercase letters, digits and hyphens, up to 64 characters. The description must be 8–320 characters. Keep each file under 64 KiB, use ordinary local folders rather than links, and avoid IDs already supplied by the bundled catalog. Click **«Обновить каталог»** after changing the configured folders.

External guides are treated as untrusted references. Their `tools` field points to existing operations; it cannot install a backend, open new permissions or turn another project's MCP command names into local commands. Adapt imported instructions to the actual host and keep their license/source information.

## Sources and licenses

| Included reference | Source |
| --- | --- |
| Adapted skills | [Besty0728/Unity-Skills](https://github.com/Besty0728/Unity-Skills), [Nice-Wolf-Studio/unity-claude-skills](https://github.com/Nice-Wolf-Studio/unity-claude-skills) — MIT. |
| Additional adapted guides and pinned recipes | [batihandev/unity-mcp-skills](https://github.com/batihandev/unity-mcp-skills) — MIT. |
| Roslyn compiler adaptation used by Editor snippets | [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) — MIT. |

Original revisions, paths and hashes are recorded in the [skill catalog](../Packages/com.worldweaver.toolkit/Editor/Skills/catalog.json), [recipe catalog](../Packages/com.worldweaver.toolkit/Editor/RecipeLibrary/catalog.json) and [import lock](skills/unity-recipes-lock.json). Licenses are retained in [skill notices](../Packages/com.worldweaver.toolkit/Editor/Skills/THIRD-PARTY-NOTICES.md) and [package notices](../Packages/com.worldweaver.toolkit/ThirdPartyNotices/).

For source contributors, these commands verify the checked-in catalogs offline:

```sh
python3 scripts/import_agent_skills.py
python3 scripts/import_unity_recipes.py
```

Rebuilding from another upstream revision is a source change: review compatibility and licensing, update the lock and verify the adapted execution path. Increasing the number of guides alone does not establish that a task can be completed.

Next: [examples](examples.md), [chat workflow](chat-workflow.md), [FAQ](faq.md).

Implementation: [skill loader](../Packages/com.worldweaver.toolkit/Editor/Assistant/AgentSkillCatalog.cs), [recipe lookup](../Packages/com.worldweaver.toolkit/Editor/Assistant/UnityRecipeCatalog.cs).
