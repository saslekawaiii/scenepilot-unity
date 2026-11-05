# Connect an API or another model server

Add a model to Unity Editor Agent from the chat's **«Добавить API…»** button. The connection form is **«Подключить модель»** in the current 1.8.1 interface.

| Protocol | Use it for |
| --- | --- |
| **OpenAICompatible** | A server implementing OpenAI-compatible **Chat Completions**. |
| **Ollama** | An Ollama endpoint on another port or behind an HTTPS proxy. Standard local Ollama is [discovered automatically](local-models.md). |

Native Anthropic Messages, Gemini and OpenAI Responses require a compatible gateway or a separate adapter; they are not direct connection types in this form. OpenAI-compatible connections currently accept **text only**.

<a id="облачные-модели-через-установленную-ollama"></a>

## Ollama cloud routes

An Ollama model ID ending in `:cloud` uses the Ollama protocol even when inference runs remotely. Keep the local Ollama app running and use its existing account access. See [local, proxy and cloud routes](local-models.md#another-port-proxy-or-cloud-model) for setup and quota behavior.

## Add the connection

1. Choose **«Тип API»** (protocol).
2. Set **«Название в списке»** to a friendly label.
3. Enter the **base URL** and **«Точный ID модели»**. Use the provider's exact model ID, not its display name.
4. For OpenAI-compatible endpoints, choose an answer format and, if required, provide one credential method below.
5. Click **«Добавить и выбрать модель»**. The connection is saved and selected; this action does not generate a response.
6. Under **«Сохранённые подключения»**, select it and test the connection.

Example values are placeholders, not a supplied service:

```text
Protocol:  OpenAICompatible
Name:      My model server
Base URL:  https://api.example.com/v1
Model ID:  the-exact-server-model-id
```

A root OpenAI-compatible URL gains `/v1`; an explicit custom path is preserved. A trailing `/chat/completions` or `/models` is removed from the base. Ollama's `/api`, `/api/chat`, `/api/tags` and `/api/show` suffixes are also normalized. Use HTTPS, or loopback HTTP for a server on this computer. URLs with embedded credentials, query strings or fragments are rejected.

## Choose the response format

| Setting | When to use it |
| --- | --- |
| **JsonObject** | The form's default for a server with JSON-object response mode. |
| **JsonSchema** | A server that supports the requested structured-output schema. |
| **Text** | A compatible server that rejects `response_format`. The returned plan is still parsed and validated. |

These settings govern the JSON-response path. Native function calls use the active tool contract instead. The server and selected model must support the mode used by the chat; selecting a schema setting cannot add missing provider capabilities.

## Test metadata before inference

| Button | What it does |
| --- | --- |
| **«Проверить соединение»** | Reads the server's model list and available capabilities without asking for a completion. |
| **«Проверить ответ»** | Makes an explicit short completion request, limited to 64 output tokens. It may consume provider quota. |

A server can implement Chat Completions without exposing `/models`. In that case a failed model-list check is not conclusive: confirm the URL and exact ID, then use the explicit answer test. A short successful answer confirms transport, not reliable Unity tool execution.

## Credentials and persistence

Choose **one** method:

- **«Ключ API»**: enter a key for this Unity process. It survives recompilation/domain reload and closing the form, but is lost after a full Unity exit.
- **«Имя переменной»**: enter the name of an environment variable that Unity already inherits, such as `MY_MODEL_API_KEY`. The value itself stays outside the connection file.
- Leave both empty for an endpoint without authentication. The Ollama adapter does not use an API key.

The package does not create a system environment variable or load a project's `.env` file for this form. To make an environment-based connection persistent, configure the launch environment that starts Unity. For a process-only key after restarting Unity, remove and re-add that connection with the key; the current form has no in-place editing flow.

Up to **12** connections are stored in `UserSettings/WorldWeaverModels/connections.json`. It contains the label, endpoint, model ID, protocol, response mode and environment-variable name. Do not place a secret in those ordinary text fields or commit local credentials. Removing a connection does not delete a user-managed external environment variable.

## Resolve a provider error

| Error | What to check |
| --- | --- |
| Model not found / HTTP 404 | Exact model ID, API base path and server model availability. |
| HTTP 401 or 403 | Credentials, account permissions and provider access restrictions. |
| HTTP 400 / unsupported `response_format` | The server's supported response format and function-call mode. |
| HTTP 429 or exhausted quota | Provider limits. The package cannot add quota; select an available route or wait as appropriate. |
| Empty or truncated answer | Model output limits and provider compatibility. An empty answer is not a completed task. |
| Missing key after restart | Restore the process credential or use an inherited environment variable. |
| Image rejected | Use a supported local Ollama vision model; this custom OpenAI-compatible adapter is text only. |

Next: [chat workflow](chat-workflow.md), [troubleshooting](troubleshooting.md), [FAQ](faq.md).

Implementation: [connection form](../Packages/com.worldweaver.toolkit/Editor/Assistant/ChatModelConnectionWindow.cs), [validation and storage](../Packages/com.worldweaver.toolkit/Editor/Assistant/ChatModelConnections.cs), [OpenAI-compatible transport](../Packages/com.worldweaver.toolkit/Runtime/Providers/OpenAICompatibleProvider.cs).
