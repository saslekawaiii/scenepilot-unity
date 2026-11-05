# FAQ

## Is Unity Editor Agent a model?

It is a Unity Editor agent host. It connects a selected language model to project inspection, editing tools, generated C#, reference skills and verification. It does not train new model weights.

## Why does the installed package still say ScenePilot or WorldWeaver?

The public project is now Unity Editor Agent. The immutable 1.8.1 package keeps its original display names, **Tools → ScenePilot → Chat** menu and `com.worldweaver.toolkit` ID so existing installs and scenes remain compatible.

## Can I use my own model or API?

Yes: local Ollama, a manually configured Ollama server, or an OpenAI-compatible **Chat Completions** endpoint. Use **Добавить API…** for custom connections. Anthropic Messages and Gemini are not directly supported by that form. A ChatGPT browser login does not configure this connection.

## Is inference free?

The addon does not include an unlimited hosted model allowance. Local models run on your hardware. Cloud models use the provider's access rules, limits and pricing, including any temporary free tier. Listed availability is not a promise of future access.

## Does “Глубоко” select a smarter model?

It changes the agent's work and response budgets. Choose the model separately. More time does not guarantee a correct result.

## Can it complete any Unity task?

Results depend on the model, project context, permissions, available tools and assets. Inspect actual changes and checks. For interactive mechanics, compilation and component presence are not substitutes for Play Mode behavior.

## Can it search online and use real assets?

It has internet research and asset tools. Internet research needs the [Python web helper](web-agent.md); assets must have supported formats and permitted licenses. Available models, textures and authored animations vary by source. A generated or imported character is not a guarantee of a production-quality rig or animation set.

## How do I add skills?

Open **Навыки → Дополнительные настройки**, add a folder containing `skill-id/SKILL.md` directories and select **Обновить каталог**. Skills are reference documents chosen for a request. They do not execute themselves, add new Unity APIs or override editing permissions.

## Where are chats and API keys stored?

Chats/settings use the project's `UserSettings/WorldWeaverChat`; connection metadata uses `UserSettings/WorldWeaverModels`. A key entered in the connection window is held in the Unity process environment and disappears when Unity closes. For persistent access, supply an environment-variable name and launch Unity with its value set. Keep credentials out of prompts and repository files.

## Does a new chat reset the scene? Does a GitHub update update my install?

A new chat does not reset your Unity scene. A GitHub push also does not replace an installed Git/TGZ dependency: update it explicitly in Package Manager. [Update guide](updating.md).

## Can I undo an agent request?

Use **Отменить этот запрос** when its checkpoint is available. Later manual edits can prevent an automatic restore. Generated-code transactions may also offer **Вернуть предыдущий код**. Keep normal version control alongside these controls.

[Getting started](getting-started.md) · [Chat workflow](chat-workflow.md) · [Troubleshooting](troubleshooting.md)
