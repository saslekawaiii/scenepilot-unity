# ScenePilot

An AI agent inside the Unity Editor. Describe a change, let the agent inspect the project, and review what Unity actually applied and verified.

## Install and open

Use Unity 6000.3 (validated on 6000.3.27f1). In Package Manager, install from Git URL:

```text
https://github.com/saslekawaiii/scenepilot-unity.git?path=/Packages/com.worldweaver.toolkit#v1.8.0
```

Or install the UPM tarball from the GitHub release. Open **Tools → ScenePilot → Chat**, select a model and write a request. The previous **Tools → WorldWeaver → Чат** entry remains available. Package ID, C# namespaces and saved data paths retain their existing names so installed projects keep their components and chat history.

## Models

Run Ollama to discover locally installed conversational models. Embedding models are not chat agents. Another OpenAI-compatible service can be added through the model connection form: base URL, exact model ID and optional API key. Availability, speed, quality and provider pricing depend on the chosen model; ScenePilot does not provide model credits.

## Workflow

**Ask** reads and explains. **Plan** proposes work. **Execute** applies authorised changes. The original request remains separate from the tentative plan. The agent discovers tools, reads real Unity state, writes C# when allowed, obtains real compilation results, and can continue after domain reload.

For APIs outside built-in tools, the agent can search Unity references, inspect actual type signatures and execute Editor C#. Repeated commands without real changes do not count as implementation progress. Failed behavioral observations remain current until the implementation changes and a fresh check is performed.

## Verification

`run_runtime_probe` executes a bounded C# iterator in real Play Mode. It records actual values, supports virtual Input System keyboard/mouse devices, and evaluates explicit assertions. Its wall budget is 0.25–120 seconds; it supports cancellation and restores the original Editor scene. Temporary test arrangements do not become the game's implementation. A successful model-authored probe confirms only the properties it exercised; independent artifact checks remain separate.

Compilation, a screenshot and the existence of a component are different kinds of evidence. None proves an arbitrary game request has been completed. Task status and operation results show what was applied, measured, or left unresolved.

## Code permissions and recovery

Autonomous Editor C# and autonomous script import require explicit project settings. Editor snippets run with Unity Editor permissions; they are not a sandbox. Use trusted requests and review generated code. Undo covers registered scene changes and does not undo arbitrary external side effects.

Checkpoints preserve supported scene and project changes, verify hashes, and stop automatic continuation on conflicting manual edits. Identical backup contents share SHA-256 storage. Limits are 300 MiB per snapshot, 20,000 files, one to eight loaded scenes and a 2 GiB shared pool. Library, model weights and arbitrary external code effects are outside this recovery mechanism. A single untitled scene is supported; Prefab Mode, baked lighting and symlinks have additional restrictions.

## Local data

- `UserSettings/WorldWeaverChat`: conversations and task state.
- `UserSettings/WorldWeaverCheckpoints`: checkpoint journals and shared content.
- `UserSettings/WorldWeaverAgentJobs`: compilation and Play results.
- `UserSettings/WorldWeaverModels`: model connection metadata; keep credentials out of source control.
- `Assets/WorldWeaverGenerated`: generated assets and runtime source.

Full model setup, web-agent installation, licenses and measured validation are in the [project documentation](https://github.com/saslekawaiii/scenepilot-unity#documentation).
