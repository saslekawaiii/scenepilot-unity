# Unity Editor Agent documentation

Build, inspect and iterate inside Unity Editor with a model you choose.

[Project home](../README.md) · [Русский](README.ru.md) · [Latest release](https://github.com/saslekawaiii/unity-editor-agent/releases/tag/v1.8.1)

## Start here

1. [Install the package](getting-started.md) and open the chat.
2. Connect [Ollama](local-models.md) or [another model API](custom-api.md).
3. Learn [Ask, Plan and Execute](chat-workflow.md), context and permissions.
4. Try an [example prompt](examples.md) and inspect the result.

## User guides

| Guide | What it covers |
| --- | --- |
| [Getting started](getting-started.md) | Requirements, Git/TGZ installation and your first conversation. |
| [Chat workflow](chat-workflow.md) | Modes, reasoning budget, context, attachments, approvals and recovery. |
| [Local models](local-models.md) | Ollama discovery, model lists and local-server connections. |
| [Custom APIs](custom-api.md) | Endpoint setup, model IDs, credentials and connection checks. |
| [Examples](examples.md) | Starting points for inspection, scenes, code, UI and gameplay. |
| [Materials and assets](material-library.md) | Built-in resources, online sources, imports and provenance. |
| [Web research](web-agent.md) | Optional Python worker, source reading and researched answers. |
| [NPCs and dialogue](npc-ai.md) | Profiles, free-form conversation, memory and runtime actions. |
| [Troubleshooting](troubleshooting.md) | Installation, provider, compilation and task failures. |
| [FAQ](faq.md) | Compatibility, costs, permissions and project scope. |
| [Updating](updating.md) | Updating Git, TGZ and local package installations. |

## Engineering reference

| Reference | Use it when |
| --- | --- |
| [Architecture](architecture.md) | Understanding the Editor agent and runtime systems. |
| [Skills and API recipes](unity-recipes.md) | Exploring and updating the reference libraries. |
| [Contributing](../CONTRIBUTING.md) | Preparing a change or a reproducible bug report. |
| [Maintaining releases](MAINTAINING.md) | Packaging and publishing a release. |
| [Verification records](validation/1.8.1/README.md) | Inspecting the 1.8.1 test results and model-run evidence. |
| [Previous release records](validation/1.8.0/README.md) | Comparing results with the earlier release. |
| [Design decisions](adr/ADR-005-chat-agent.md) | Understanding the original chat-agent design. |
| [Licensing and credits](LICENSING.md) | Checking original-code status and third-party notices. |

## Project name and package compatibility

Unity Editor Agent is the project's current public name. Release 1.8.1 still opens through **Tools → ScenePilot → Chat** and retains the package ID `com.worldweaver.toolkit`. Historical release notes, screenshots and records retain the names used when they were created.
