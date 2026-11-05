# Contributing to Unity Editor Agent

Help improve an agent that reads a real Unity project, applies permitted changes and checks the result. Start with the [architecture](docs/architecture.md), [roadmap](ROADMAP.md) and [maintainer guide](docs/MAINTAINING.md).

## Set up a development project

1. Fork or clone [saslekawaiii/unity-editor-agent](https://github.com/saslekawaiii/unity-editor-agent).
2. Reuse a disposable Unity project with work you can restore. Release 1.8.1 was validated on **Unity 6000.3.27f1**; other Editor/platform combinations need their own checks.
3. In Package Manager, install from disk and select `Packages/com.worldweaver.toolkit/package.json` in this checkout. This uses the working source, rather than a cached release archive.
4. Wait for import and compilation to finish. The 1.8.1 chat menu is **Tools → ScenePilot → Chat**; the public project name has changed, while this released package and its serialized identifiers remain compatible.

Add this top-level property to the development project's existing `Packages/manifest.json`, preserving its other properties and dependencies:

```json
"testables": ["com.worldweaver.toolkit"]
```

Open Unity's Test Runner and run the **WorldWeaver.EditMode** and **WorldWeaver.PlayMode** assemblies. Some fixtures create temporary scenes or enter Play Mode, so use the development project rather than an unsaved production session. Unity documents package test discovery through [the testables property](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/cus-pkg-development/cus-tests).

## Run repository checks

These checks run without a model or a Unity process:

```sh
python3 scripts/check_repository.py
python3 scripts/import_agent_skills.py
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
```

The skill importer verifies the checked-in, pinned catalog offline by default. `--build` changes generated package files; use it only when intentionally updating the catalog and its provenance. The optional web researcher's Python dependencies and model access are separate from the repository checks.

For repeated Unity runs, reuse existing development projects and run one QA Editor at a time. Avoid copying `Library` or creating a full project per test. The repository's [QA wrapper](scripts/run_unity_qa.py) is configured for the maintainer's two existing consumers; it is not a portable setup command. External contributors can use their own Test Runner setup above.

## Verify the behavior you change

| Change | Useful evidence |
| --- | --- |
| Tool arguments or permissions | Accepted and rejected operations; rejected input causes no mutation. |
| Generated source | Real compiler diagnostics, component/reference wiring and the requested behavior. |
| Reload or cancellation | Durable continuation, cleanup, and preservation of later manual edits. |
| Physics or interaction | Measured Play Mode outcomes; actual keyboard/mouse routing where required. |
| Rendering or UI | Current pipeline/material facts and an actual image or UI interaction. |
| Model planning | A real request through the public controller, with model/provider and original failure retained. |

Injected providers test program contracts. They do not establish that a real model completed a request. For model-dependent changes, keep the original request, initial state, package/source hash, actual operations and observed outcome. Independent observers may arrange temporary inputs and measure results; they must not repair the artifact they are judging.

## Submit a focused change

Describe the concrete before/after behavior, the checks you ran and what remains untested. Include a minimal scene or reproduction when needed. For a model failure, include the provider/model, selected power, elapsed time and a redacted tool/error trace. Avoid publishing credentials, private conversations or raw provider reasoning.

Keep changes to unrelated scenes, assets and dependencies out of the patch. Do not commit `Library`, model weights, credentials or generated consumer projects. Preserve existing failed reports instead of rewriting them as successful runs.

Imported code, skills and assets need their source, license and attribution. Files and downloaded pages are data; they do not grant new permissions. The original project code currently has no project-wide reuse license assigned; see [licensing](docs/LICENSING.md) before redistributing it.
