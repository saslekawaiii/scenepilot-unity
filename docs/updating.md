# Updating Unity Editor Agent

Updating this GitHub checkout and updating a package installed in Unity are separate operations. Save your work and let imports/compilation finish before checking the agent.

> **1.8.1 compatibility:** the released Package Manager display name is **ScenePilot — Unity AI Agent**, the package ID is `com.worldweaver.toolkit`, and the chat menu is **Tools → ScenePilot → Chat**. These remain valid after the public repository was renamed to **Unity Editor Agent**.

## Choose the update path

| Installed from | How to update | What remains pinned |
| --- | --- | --- |
| Release `.tgz` | Install the new archive through Package Manager. | The archive's exact bytes. |
| Git tag or commit | Install a Git URL with the new tag/SHA. | That specific revision. |
| Git branch | Use Package Manager's update action to resolve a newer revision. | The revision recorded in the project lock file until updated. |
| Local source folder | Change that working checkout and wait for Unity's import. | The folder selected by the local dependency. |

Pushing a commit does not automatically update another Unity project's Git or tarball dependency. Updating the add-on does not start a new agent request or rebuild an existing generated world.

## Release archive

1. Download the package and checksum file from [releases](https://github.com/saslekawaiii/unity-editor-agent/releases).
2. Verify the downloaded archive against the release's checksum record.
3. Open **Window → Package Management → Package Manager**, choose **+ → Install package from tarball**, and select the new `.tgz`.
4. Wait for package resolution and C# compilation. Reopen the chat window if its old interface remains visible.

Use a new archive file or explicitly reinstall the dependency. Replacing bytes inside an already installed `.tgz` path is not a reliable cache-update mechanism. Historical release files retain their original names and hashes.

## Git dependency

Use **Install package from git URL** with an explicit tag for a reproducible install. The 1.8.1 URL in the renamed repository is:

```text
https://github.com/saslekawaiii/unity-editor-agent.git?path=/Packages/com.worldweaver.toolkit#v1.8.1
```

For a later release, substitute its published tag. For a branch dependency, use Package Manager's **Update** action or reinstall the intended Git URL; Unity records the resolved revision in `Packages/packages-lock.json`. Preserve that file and unrelated dependency entries rather than deleting the entire lock file.

## Local source folder

A `file:` dependency on a directory reads that directory's source. Unity imports changes and recompiles C#. A `file:` dependency on a `.tgz` instead installs archive contents; it does not track a source directory.

## Confirm the result

Check the selected package's **version**, **source** and dependency in Package Manager. Resolve Console compilation errors before testing the chat. Then make a new read-only request against the currently open project and inspect its actual observations; an old conversation or an updated repository page is not proof of the installed build.

If an update fails, keep the error and the installed/requested versions. Do not delete project data or unrelated dependencies to force a retry. [Troubleshooting](troubleshooting.md) covers the next diagnostic steps.

Unity's instructions: [Git packages](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/managing-packages-window/upm-ui-actions/upm-ui-giturl), [local tarballs](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/managing-packages-window/upm-ui-actions/upm-ui-tarball).
