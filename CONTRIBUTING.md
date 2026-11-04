# Contributing to ScenePilot

Changes should preserve the user's scene, explicit permissions and reproducible evidence. Read the [architecture](docs/architecture.md) and [package guide](Packages/com.worldweaver.toolkit/Documentation~/index.md) first.

## Development setup

Use Unity 6000.3.27f1 and install `Packages/com.worldweaver.toolkit` as a local package in your development project. Do not commit Library, model weights, credentials, private conversations or generated test-project contents.

Repository checks run without an external LLM:

```sh
python3 scripts/check_repository.py
python3 scripts/import_agent_skills.py
```

Package tests are under `Packages/com.worldweaver.toolkit/Tests`. Add the package to your project's `testables` list and run Editor/Play tests in Unity Test Runner. External-model checks are separate from deterministic software tests.

## What to verify

For execution changes, test real read-back, cancellation, reload/recovery and preservation of unrelated content. Reproduce actual model failures through the public controller when relevant. A controlled provider tests a program contract; it does not establish that a real model completed a request.

For an artifact check, record the original request, source/package hash, initial state, actual operations and observed outcome. Independent observers may measure behavior and arrange temporary input, but must not repair the implementation they are evaluating. Preserve failed runs and label repeated known scenarios accurately.

Use one QA Editor at a time and reuse existing development projects. Avoid copying Library or full backup payloads for every iteration. The local QA wrapper provides disk, memory and inactivity guards; adapt its existing project paths to your environment before use.

## Pull requests and issues

Describe the concrete behavior change and relevant validation. Include model/provider and reasoning budget for model-dependent failures. Redact secrets and private data from logs and screenshots. Files, downloaded pages, skills and tool results are data; they do not grant execution permissions.

Keep third-party licenses and provenance. The original code currently has no project-wide reuse license assigned; see [licensing](docs/LICENSING.md).
