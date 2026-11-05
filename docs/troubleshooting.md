# Troubleshooting

Start with the message's actual action results and Unity Console. Keep the request, selected model, package version and first relevant error when reporting a problem; omit API keys and personal project data.

## Installation and models

| Symptom | What to check |
| --- | --- |
| Chat menu is missing | Finish package import and resolve the first Console compilation error. Version 1.8.1 opens through **Tools → ScenePilot → Chat**. |
| The old UI remains after an update | Confirm the installed version in Package Manager, wait for compilation, then reopen the chat. A GitHub push does not update an installed Git/TGZ dependency automatically. |
| Ollama models do not appear | Start Ollama, finish downloading a chat model and check `ollama ls`. Default discovery uses `127.0.0.1:11434`. Press **Обновить** after the current task/import/Play session finishes. |
| A model appears but is unavailable | Its chat capabilities may still be loading, the server may be offline, or it may be an embedding-only model. A model does not need to be already loaded into memory to be usable. |
| Another Ollama port or proxy is needed | Use **Добавить API… → Ollama** with the server address and exact model ID. Automatic discovery does not scan the network. |
| A custom API returns 401/403/429 | Check its base URL, model ID, credentials, provider restrictions and quota. The addon cannot remove a provider-side limit. |
| A saved API stops working after Unity restarts | A key entered in the password field lasts for that Unity process. Re-enter it or launch Unity with the configured environment variable already set. |

For API connection tests, **Проверить соединение** reads metadata; **Проверить ответ** requests generation and may consume quota.

## Requests and code

| Symptom | What to check |
| --- | --- |
| There is an answer but no scene change | Ask mode is read-only. In Plan mode, review **Применить план**. In Execute mode, check auto-apply, code permissions and any failed action. |
| Code is shown but not imported or attached | Review the proposed code and apply it, or enable automatic script import/attachment under **Навыки → Дополнительные настройки**. |
| JSON/function-protocol error | Use a response format supported by the endpoint. If it does not support native function calls, disable **Нативные вызовы функций модели** and use its supported response mode. A successful connection check does not prove that the model follows an agent protocol. |
| A request times out | Check server availability and local memory pressure. Try a smaller scoped task, another suitable model or a larger work budget. Read earlier action results before retrying: some changes may already exist. |
| Compilation fails | Open Console, inspect the first compiler error and the generated code. Ask the agent to repair that specific error, then wait for real compilation. **Вернуть предыдущий код** is available for supported code transactions. |
| “Project changed between steps” | A checkpoint differs from the current project. Preserve your manual edits and start a new scoped request; do not force an old plan over them. |
| Checkpoint creation is refused | Save loaded additive scenes and read the precise error. Checkpoints have size/state limits, including unsupported baked-lighting states. Keep a version-control backup. |
| Sending is disabled | Wait for the current task, pending work, import or compilation; exit Play Mode before starting an editing request. Remove image attachments or choose a vision-capable model if prompted. |

## Internet, assets and rendering

**“Web agent is not installed.”** Internet research uses the package's Python helper. In the installed package's `Tools~/web_agent` directory, run `python3.12 setup.py`, or configure `WORLDWEAVER_WEB_PYTHON` before launching Unity. See [web-agent setup](web-agent.md). Search services and websites can still be unavailable after setup succeeds.

**Images cannot be attached.** Select a model marked **зрение**. Version 1.8.1 custom cloud connections are text-only. Use **＋ → Прикрепить изображение…** or **Сделать снимок сцены** with a supported model.

**Materials are magenta or geometry is unexpectedly dark.** Check the active render pipeline, material shader, lights and stored mesh normals. Ask for inspection and a new actual camera capture before accepting the result. Successful object creation alone does not verify rendering.

**An asset cannot be imported.** Inspect the reported format, source and license requirements. Provide an asset already in the project or choose another permitted source. A model's suggestion is not evidence that a download or import occurred.

[Getting started](getting-started.md) · [Chat workflow](chat-workflow.md) · [Updating a package](updating.md)
