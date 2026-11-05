<p align="center">
  <img src="docs/assets/brand/hero.svg" alt="Unity Editor Agent — build in Unity with an AI agent" width="100%" />
</p>

<p align="center">
  <a href="https://github.com/saslekawaiii/unity-editor-agent/releases/tag/v1.8.1"><img src="https://img.shields.io/badge/preview-1.8.1-8b9cff?style=flat-square" alt="1.8.1 preview" /></a>
  <a href="docs/getting-started.md#requirements"><img src="https://img.shields.io/badge/Unity-6-222c37?style=flat-square&amp;logo=unity&amp;logoColor=white" alt="Unity 6" /></a>
  <a href="docs/local-models.md"><img src="https://img.shields.io/badge/models-local_%2B_cloud-59d6b3?style=flat-square" alt="Local and cloud models" /></a>
  <a href="https://github.com/saslekawaiii/unity-editor-agent/actions/workflows/repository-checks.yml"><img src="https://github.com/saslekawaiii/unity-editor-agent/actions/workflows/repository-checks.yml/badge.svg" alt="Repository checks" /></a>
</p>

<p align="center">
  <b>English</b> · <a href="README.ru.md">Русский</a><br />
  <a href="docs/getting-started.md">Get started</a> · <a href="docs/README.md">Documentation</a> · <a href="docs/examples.md">Examples</a> · <a href="https://github.com/saslekawaiii/unity-editor-agent/issues">Issues</a>
</p>

**An AI agent that works inside Unity Editor.** Describe a task in chat, let the agent inspect your project, and work with editable scenes, assets and C# scripts. Connect your own local model or an OpenAI-compatible API.

The agent uses Unity tools, compiler feedback and Play Mode observations to work through a task. You can ask questions, review a plan, or let it apply changes in the current project.

## Get started

### 1. Install in Unity

Open **Window → Package Management → Package Manager**, choose **+ → Install package from Git URL**, and paste:

```text
https://github.com/saslekawaiii/unity-editor-agent.git?path=/Packages/com.worldweaver.toolkit#v1.8.1
```

Using a downloaded package? Install the `.tgz` from [Releases](https://github.com/saslekawaiii/unity-editor-agent/releases/tag/v1.8.1). See the [installation guide](docs/getting-started.md) for requirements and updating an existing installation.

### 2. Connect a model

Start **Ollama** to discover installed chat models, or choose **Добавить API…** in the chat window to add an OpenAI-compatible endpoint.

[Local models →](docs/local-models.md) · [Connect another API →](docs/custom-api.md)

### 3. Open the chat

In the current **1.8.1** package, open **Tools → ScenePilot → Chat**. Choose a model and describe what you want to create or change.

> Unity Editor Agent is the new project name. The existing package still uses the ScenePilot menu. Its package ID and saved data remain compatible.

## What you can work on

| Area | Tools available to the agent |
| --- | --- |
| **Scenes & levels** | Objects, Terrain, meshes, lights, materials, colliders and scene references. |
| **C# development** | Read project code, inspect Unity APIs, draft scripts, compile and attach components. |
| **Assets & research** | Search project assets, read web sources and import supported models, textures and heightmaps. |
| **Gameplay** | Player controls, cameras, UI interactions, NPC profiles, dialogue and navigation. |
| **Verification** | Inspect compiler errors, capture camera views and run bounded Play Mode probes with keyboard/mouse input. |
| **Project context** | Use the selected objects, scene state, relevant source files, skills and Unity API recipes. |

Browse the [workflow guide](docs/chat-workflow.md) for selection context, attachments, model budgets and project permissions.

## Three ways to work

| Mode | Use it for |
| --- | --- |
| **Ask · Спросить** | Explain code, inspect the project or research an API. |
| **Plan · Спланировать** | Review the proposed steps before approving changes. |
| **Execute · Выполнить** | Apply changes and inspect the resulting tool output and checks. |

Start with a small, observable result. Follow up in the same chat to refine it.

```text
Add three point lights along the selected corridor. Keep the existing
objects and materials. Check that the lights are above the floor.
```

```text
Explain how the selected player controller handles jumping.
Read its source and suggest changes without editing the project.
```

[More example prompts →](docs/examples.md)

## Documentation

| Start here | Go further |
| --- | --- |
| [Installation & first chat](docs/getting-started.md) | [Chat workflow & permissions](docs/chat-workflow.md) |
| [Ollama & local models](docs/local-models.md) | [Custom model APIs](docs/custom-api.md) |
| [Examples](docs/examples.md) | [Materials & asset sources](docs/material-library.md) |
| [Troubleshooting](docs/troubleshooting.md) | [Web research](docs/web-agent.md) |
| [FAQ](docs/faq.md) | [NPCs & dialogue](docs/npc-ai.md) |

**Preview:** model capability and response time vary. Review generated code and applied changes. Provider accounts and inference usage are managed separately. See the [FAQ](docs/faq.md) for support scope.

## Development

[Contributing](CONTRIBUTING.md) · [Architecture](docs/architecture.md) · [Skills & API recipes](docs/unity-recipes.md) · [Roadmap](ROADMAP.md) · [Changelog](CHANGELOG.md)

Release verification records are available in the [technical documentation](docs/README.md#engineering-reference).

## Credits & licensing

The project uses components and reference material from the Unity and AI tooling communities. Third-party notices ship with the package. The original project code currently has no project-wide reuse license assigned; see [licensing and credits](docs/LICENSING.md) before redistributing it.
