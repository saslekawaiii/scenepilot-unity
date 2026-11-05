# Working in the chat

Unity Editor Agent works with the open Unity project. Describe the outcome, identify what should stay unchanged, and ask for checks that matter to the task.

In 1.8.1, open **Tools → ScenePilot → Chat**. The current Russian controls are listed below.

## Choose a mode

| Control | Use it for |
| --- | --- |
| **Спросить** — Ask | Inspect the project, research or explain without changing it. |
| **Спланировать** — Plan | Review proposed changes before applying them with **Применить план**. |
| **Выполнить** — Execute | Let the agent apply permitted changes and check the result. |

A proposed plan is not an applied change. Compiled code is not proof that a game mechanic behaves correctly.

The **Быстро / Баланс / Глубоко** selector controls the agent's work and response budgets. It does not change a model's parameter count or turn it into a different model. Select the actual model separately in **Модель помощника**.

## Give useful context

The **＋** menu can insert a task starter, attach selection context, import an image or capture the scene. Task starters are editable prompt prefixes; ordinary chat does not require selecting one.

Select an object in Hierarchy, then choose **Прикрепить контекст выделения** when the request concerns that object. Use stable names in follow-up requests: “Keep `Player` and modify only `Beacon`” is clearer than “change that one.”

Images require a model marked **зрение**. Up to three images can accompany a request. Custom cloud connections in this release are text-only; local Ollama vision support is enabled after its model capabilities are checked.

## Build, inspect, then refine

1. State the outcome and scope: which scene or object, expected behavior, permitted assets and constraints.
2. Use Plan when you want to inspect a proposal first; use Execute when the editing scope is clear.
3. Read the task status and **Результаты действий**. Check which tools actually ran, what changed and which checks failed or remain unconfirmed.
4. Inspect the Scene/Game view. For interactive behavior, ask for actual Play Mode checks and try the controls yourself.
5. Refine the existing result with a follow-up that names the remaining problem.

Ask explicitly to save the scene when that is part of the task. Created objects and generated assets do not by themselves mean the current scene has been saved.

## Enable code deliberately

Open **Навыки → Дополнительные настройки**:

| Setting | Effect |
| --- | --- |
| **Проверять результат автоматически** | Enables automatic verification. |
| **Создавать C# для новых игровых механик** | Encourages generated scripts when a mechanic needs them. |
| **Импортировать и подключать игровые скрипты автоматически** | Allows automatic runtime-script import and attachment. Otherwise code can wait for review. |
| **Автономный C# с доступом Unity Editor** | Enables generated Editor C# and runtime probes for operations beyond built-in tools. |
| **Применять план в режиме «Выполнить»** | Controls automatic application in Execute mode. |

The two autonomous code permissions are off by default. Editor C# executes with Editor access; it is not a sandbox. Enable it for projects where you accept that access. Skills supply reference knowledge and do not grant permissions.

Unity may compile and reload scripts during a task. Wait for that work to finish before sending another request.

## Stop, continue or restore

- Use **Escape** or the stop control to cancel the current request. Earlier applied steps may remain.
- Use **Продолжить и проверить** when an unfinished task offers continuation. Review applied steps before retrying the whole request.
- Use **Отменить этот запрос** to restore an available task checkpoint. Later manual changes can block restoration or continuation to protect your work.
- **Вернуть предыдущий код**, when available, restores the earlier generated C# and triggers compilation again.

If a checkpoint reports a conflict, keep your manual edits and start a new scoped request from the current state. Checkpoints complement version control; they do not replace it.

[Example prompts](examples.md) · [Troubleshooting](troubleshooting.md) · [FAQ](faq.md)
