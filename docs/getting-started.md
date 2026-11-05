# Getting started

[English](getting-started.md) · [Русский](getting-started.ru.md)

Install Unity Editor Agent in an existing Unity project, connect a model, and start with a read-only inspection.

The public project name is **Unity Editor Agent**. Version 1.8.1 still uses **ScenePilot** in Package Manager and the Editor menu. Its package ID remains `com.worldweaver.toolkit`.

## Requirements

- Unity **6.3** (`6000.3`). The release was checked with 6000.3.27f1.
- A chat model through local Ollama or an OpenAI-compatible Chat Completions endpoint.
- Git installed if you choose the Git URL installation method.

Save your scene and commit or back up your project before the first editing task. A separate Unity project is not required.

## Install the package

### Git URL

Open **Window → Package Management → Package Manager**, then **+ → Install package from git URL**. Paste:

```text
https://github.com/saslekawaiii/unity-editor-agent.git?path=/Packages/com.worldweaver.toolkit#v1.8.1
```

Wait for dependency import and compilation to finish. This URL pins version 1.8.1; it does not follow later commits. Unity's [Git package installation guide](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-giturl.html) covers Git setup and installation errors.

### TGZ archive

Download `com.worldweaver.toolkit-1.8.1.tgz` from the [1.8.1 release](https://github.com/saslekawaiii/unity-editor-agent/releases/tag/v1.8.1). In Package Manager, choose **+ → Install package from tarball** and select that file. See Unity's [tarball installation guide](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-tarball.html).

This GitHub distribution is not a Unity-signed registry package, so Unity may show an unsigned-package notice. Use the linked repository or release and its published checksums.

## Open the chat

Choose **Tools → ScenePilot → Chat**. The older **Tools → WorldWeaver → Чат** entry opens the same window. The current interface uses Russian labels.

If the menu is missing, resolve the first compilation error in Unity Console and reopen the window.

## Connect a model

### Local Ollama

Install and start [Ollama](https://docs.ollama.com/quickstart), then install a chat model appropriate for your hardware. With Ollama running, replace `MODEL_ID` with your chosen model ID:

```sh
ollama pull MODEL_ID
ollama ls
```

If you use the server from a terminal, start it with `ollama serve`; an already running Ollama app does not need a second server. [Ollama CLI reference](https://docs.ollama.com/cli).

Models served at `http://127.0.0.1:11434` appear automatically in **Модель помощника**. Select one. **Обновить** refreshes the list; automatic discovery waits while Unity imports, compiles, runs Play Mode or processes an agent task. Installation and loading into memory are different states: the first request can take longer.

### Your own API

Click **Добавить API…** and choose **OpenAICompatible**. Enter a display name, the provider's API base URL and the exact model ID. Use the response format the server supports: `JsonSchema`, `JsonObject` or `Text`.

Enter either an API key for this Unity session **or** the name of an existing environment variable. Keep credentials out of the URL. **Добавить и выбрать модель** saves the connection; **Проверить соединение** checks metadata without generation. **Проверить ответ** makes a small inference request and can consume provider quota.

Custom cloud connections in 1.8.1 accept text. Anthropic Messages and Gemini protocols are not direct adapters in this form. Provider pricing, free tiers and availability remain provider-controlled.

## Send the first request

Choose **Спросить** and send:

> Inspect the current scene and explain its main objects. Do not change the project.

Use **Enter** to send and **Shift+Enter** for a new line. Once inspection works, follow [the chat workflow](chat-workflow.md) or try an [example prompt](examples.md). For installation or connection problems, use [troubleshooting](troubleshooting.md).
