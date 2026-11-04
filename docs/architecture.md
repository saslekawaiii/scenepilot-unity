# ScenePilot architecture

ScenePilot separates model planning, execution and verification. The original request stays in persistent task state; the model's proposed stages are tentative and cannot grant additional permissions.

## Request loop

1. Read bounded project context and discover relevant tools or API references.
2. Validate the proposed operation against its schema, current state and permissions.
3. Execute in Unity and record real read-back, changed scene/project state and errors.
4. Compile generated runtime source and preserve continuation through domain reload.
5. Exercise the requested behavior in Play Mode and compare measured results with the request.
6. Repair observed gaps within the task budget, or report the remaining work with evidence.

## Core components

| Component | Responsibility |
| --- | --- |
| ChatController | Model requests, stage progression, recovery and final review. |
| ProjectContextIndex | Bounded queries over actual objects, assets, scripts and types. |
| SceneToolExecutor / EditorCodeTools | Validated built-in operations and permitted compiled Editor snippets. |
| ScriptDrafts / source patches | Source review, actual compilation and reload continuation. |
| AgentEditorJobs | Durable Unity jobs, test results and runtime-probe measurements. |
| AgentCheckpoints | Hash-verified supported recovery and shared backup content. |
| Runtime systems | Player, NPC dialogue/actions, memory, retrieval and navigation. |

## Progress and evidence

A read of unchanged state does not count as implementation. Identical successful Editor reads retain command/observation fingerprints and can be rejected before replay. Repeating an unchanged plan also does not advance the task.

After repeated semantic or unavailable-tool errors, one fresh native inference epoch can retain the original request, real operations and current observations while dropping stale opaque provider continuation. Tool errors do not automatically imply the native transport is broken.

Only an actual implementation change makes an earlier failed behavioral observation historical. Fresh checks are needed after repair. Model-authored assertions establish only the properties they exercised; independent artifact acceptance is a separate layer.

## Runtime probes

`run_runtime_probe` compiles a bounded iterator, enters real Play Mode and exposes measured `Record`/`Check` operations plus temporary Input System devices. The wall budget is 0.25–120 seconds. Empty checks, failed assertions, exceptions, timeouts and cancellation do not produce success. The job exits Play Mode and restores the Editor scene before its final result.

Yield-based timing cannot interrupt blocking synchronous C#. Editor snippets run with full Editor permissions, so autonomous code is an explicit trust setting rather than a sandbox.

## Recovery and resources

Supported checkpoints have limits of 300 MiB per snapshot, 20,000 files, one to eight scenes and a 2 GiB shared content pool. Identical contents are stored once by SHA-256. Manual conflicts stop automatic continuation. Library, model weights and arbitrary external code effects are outside this recovery boundary.

Development acceptance reuses existing projects, limits concurrent QA Editors and stops only owned processes on resource exhaustion or prolonged inactivity. The user's Editor remains outside that process guard.

## Open components

The optional web researcher executes Hugging Face `smolagents`. The Unity reference catalogs include attributed MIT material from Unity-Skills and related sources. MCP for Unity and OpenHands were also studied as architecture references; they are not advertised as installed runtimes. Bundled assets and downloaded resources retain their respective provenance and licenses. See [licensing](LICENSING.md) and [Unity recipes](unity-recipes.md).
