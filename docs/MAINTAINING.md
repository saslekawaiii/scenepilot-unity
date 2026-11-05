# Maintaining Unity Editor Agent

This guide records the boundaries a release or documentation change must preserve. Use [CONTRIBUTING](../CONTRIBUTING.md) for development setup and [architecture](architecture.md) for the agent loop.

## Keep public identity and package compatibility distinct

| Identifier | Current value |
| --- | --- |
| Public project | Unity Editor Agent |
| Repository | [saslekawaiii/unity-editor-agent](https://github.com/saslekawaiii/unity-editor-agent) |
| UPM package | `com.worldweaver.toolkit` |
| Runtime / Editor assemblies | `WorldWeaver.Runtime` / `WorldWeaver.Editor` |
| Released 1.8.1 display/menu | `ScenePilot — Unity AI Agent` / `Tools → ScenePilot → Chat` |

Update current documentation, navigation and repository links when branding changes. Preserve historical release notes, validation, notices and archive bytes. Renaming serialized types, package identity or the shipped interface requires a separate compatibility-aware package release.

## Match each claim to its evidence

- **Software tests:** deterministic fixtures, including injected providers. Record actual pass/fail/skip counts and why any checks were skipped.
- **Real model runs:** retain the original request, model/provider, selected power, initial state, actual operations, errors and final outcome. A passing fixture is not a model success.
- **Independent acceptance:** evaluate the produced artifact without repairing it. Record actual geometry/rendering/input/Play measurements and any untested behavior.
- **Native installation:** verify installed package files and loaded assemblies separately from chat UI operation. A Package Manager screenshot does not establish that a new chat request completed.

A continuation or later observer keeps the original artifact/model-run attribution. Failed runs stay failed in the historical record. Publish safe summaries rather than raw provider reasoning, private conversations or credentials. The [acceptance summarizer](../scripts/summarize_agent_acceptance.py) writes a new output and refuses to overwrite an existing report.

## Test within a bounded environment

Run the [repository checks](../CONTRIBUTING.md#run-repository-checks), then the package's Editor/Play assemblies in an existing disposable Unity development project. Enable the package in the manifest's `testables` list as described in the contributor guide.

Reuse consumer projects and run one QA Editor at a time. Keep disk/memory/inactivity guards around repeated command-line QA. The [guarded QA wrapper](../scripts/run_unity_qa.py) has two maintainer-specific consumer paths; external contributors should use their own Test Runner setup instead of recreating those private directories. Never copy `Library` per scenario or terminate a separate user Editor to free a QA slot.

When the agent changes files or gameplay, verify preservation of unrelated content and relevant later manual edits. Checkpoint support has explicit limits; it is not a whole-computer backup.

## Freeze and publish a package

1. Freeze an exact package source commit and file manifest before release acceptance. Record the source commit, archive SHA-256 and file hashes alongside each result.
2. Test the frozen package in the supported Unity environment, with fresh output names. A changed package needs a new candidate; do not relabel an earlier result.
3. Recheck independent artifacts and real model behavior where relevant. Keep original model versions when a fresh observer evaluates an older artifact.
4. Publish the accepted archive bytes and checksums. If the release commit also contains later documentation, record the package's own source commit separately.
5. Download the published assets and compare them with the accepted local bytes. Check the release tag, links and CI conclusions. Preserve previous releases and assets.
6. Treat updating a user's installed Unity package as a separate operation, with saved work, installed-file/version checks and a new UI-level request where appropriate.

The [archive builder](../scripts/build_upm_archive.py) reads committed package bytes and checks them against a supplied snapshot. Read its `--help` and supply explicit source, snapshot, revision, archive and proof paths: its default artifact names belong to an older release. Its optional consumer-creation mode is not the routine QA workflow. Never rebuild an already published archive in place to alter its contents.

## Documentation review

Check links, image paths and heading anchors in changed Markdown. Use current repository links in active guides, and keep old names only where they identify a released UI, serialized API or historical artifact. Verify installation and test instructions against the checked-in package rather than assuming a new repository name changed the shipped menu.

Imported code, assets and advisory catalogs retain their license and provenance records. The original project code currently has no assigned project-wide reuse license; see [LICENSING](LICENSING.md) before adding a badge or describing distribution rights.
