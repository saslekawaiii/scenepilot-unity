# Local models with Ollama

Unity Editor Agent discovers conversational models from an existing local Ollama server. You do not need to register each model as a custom API.

In the current 1.8.1 package, open **Tools → ScenePilot → Chat**. The shipped chat labels are in Russian; the model selector is **«Модель помощника»**.

## Connect a local model

1. [Install Ollama](https://docs.ollama.com/quickstart) and start the app. If you run the server from a terminal, use `ollama serve`; a second server is unnecessary when the app is already serving requests.
2. Install a conversational model from the [Ollama model catalog](https://ollama.com/search). Replace `<model-id>` with its exact ID:

   ```sh
   ollama pull <model-id>
   ollama list
   ```

3. Keep Ollama running, open the chat and select the installed model. **«Обновить»** refreshes the list manually.
4. Start with **«Спросить»** (Ask) and a short project question, then use **«Выполнить»** (Execute) for an editing task. See the [chat workflow](chat-workflow.md).

Model downloads use disk space; loading a model also needs memory alongside Unity. Select a model your machine can run and check its tool use on a small task. Discovery confirms availability, not task quality.

## What appears automatically

The default discovery address is `http://127.0.0.1:11434`. The package does not scan your network or other ports.

| State | What you see |
| --- | --- |
| Installed conversational model | Available after its capabilities are checked. It can load on the first request. |
| Model already loaded by Ollama | A separate “loaded in memory” state. |
| Embedding-only model | Not offered as a conversational model. |
| Capability check pending or failed | Unavailable until the check succeeds. |
| Server temporarily stopped | Previous entries remain unavailable; they are not treated as deleted. |
| Selected model removed | Its unavailable entry is retained so the choice is not silently replaced. |

The open chat checks about every **10 seconds** when the server responds and **30 seconds** when it does not. Background checks pause during agent work, compilation, import and Play Mode. Large catalogs can take several polls to finish capability checks. Your selected model and saved custom connections are preserved.

Discovery reads metadata only: [`/api/tags`](https://docs.ollama.com/api/tags) lists installed models, [`/api/ps`](https://docs.ollama.com/api/ps) lists loaded models, and `/api/show` supplies capabilities. It does not pull models or send generation requests. An empty loaded-model list is therefore compatible with a usable installed model.

## Another port, proxy or cloud model

Use **«Добавить API…» → Ollama** for a different endpoint and an exact model ID. See [custom connections](custom-api.md). The adapter accepts HTTPS or HTTP on this computer; it does not attach a Bearer API key to Ollama requests.

A model ending in `:cloud` can still be reached through the local Ollama app. Its inference runs remotely and depends on that service's account access and quota. A loopback URL does not make a cloud model local, and the package does not purchase access or guarantee a free tier.

For screenshots, the local Ollama model must actually advertise vision support. A model can support chat without supporting images. Changing the editor chat model also does not reassign existing [NPC model profiles](npc-ai.md).

## Common connection problems

| Symptom | Next step |
| --- | --- |
| “Ollama does not respond” | Start the app/server and confirm `http://127.0.0.1:11434/api/tags` responds. |
| A newly pulled model is missing | Wait for the pull and capability checks to finish, then refresh while Unity is idle. |
| The model stays unavailable | Check its exact ID and whether Ollama reports conversational completion capability. |
| A model is installed but not in memory | Send a request; preloading it with `ollama run` is not required. |
| Requests are slow or run out of memory | Try a model that fits the available memory, close unused model sessions and reduce the task size. |

See [troubleshooting](troubleshooting.md) for request errors and [getting started](getting-started.md) for installation.

Implementation: [discovery service](../Packages/com.worldweaver.toolkit/Editor/Assistant/OllamaModelDiscovery.cs), [refresh lifecycle](../Packages/com.worldweaver.toolkit/Editor/Assistant/ChatController.ModelDiscovery.cs).
