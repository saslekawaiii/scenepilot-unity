# Web research and downloaded resources

Unity Editor Agent has an optional Python worker for real web search, page reading and bounded research. Multi-step research uses Hugging Face **smolagents `ToolCallingAgent`**; the Unity host retains control of scene changes and imports.

## Install the optional worker

Use **Python 3.12**. Locate the installed package on disk and open its `Tools~/web_agent` directory. From that directory run:

```sh
python3.12 setup.py
```

The installer creates a per-user virtual environment under `~/.local/share/worldweaver/web-agent/venv` and installs the pinned `requirements.lock`. It does not need administrator access. On Windows, the interpreter is in `Scripts/python.exe`; on macOS/Linux it is in `bin/python`.

For an existing compatible environment, set **`WORLDWEAVER_WEB_PYTHON`** to its interpreter path in the environment inherited by Unity. Installing Python alone is insufficient: the worker dependencies must be installed in the interpreter Unity uses.

## Pick the right operation

| Tool | Result | Requires a research model? |
| --- | --- | --- |
| `web_search` | Search results with observed URLs and snippets. | No |
| `web_read` | A public page's text and actual links. | No |
| `web_research` | A bounded search/read loop using smolagents. | Yes, through local Ollama |
| `asset_download` | A supported data file, source receipt and SHA-256. | No |
| `terrain_from_heightmap` | Measured elevation applied to an existing generated Terrain. | No |

The main chat still uses its selected model to decide which operation to request. The worker's **research** model is a separate choice: `web_research` accepts an exact Ollama `model` ID and currently defaults to `beam:cloud` through `http://127.0.0.1:11434`. To keep research local, explicitly request an installed local model that supports tool calls. The package does not download it, sign in or buy access; cloud availability and quotas remain provider-controlled.

## Ask for sources, then inspect the result

For a researched explanation, use **Ask** or explicitly prohibit project edits:

> Only explain; do not change the project. Find and read official Unity documentation for the relevant APIs. Compare the behavior and link the pages actually opened. Do not enter Play Mode.

For a resource task, specify the destination and usage rights:

> Find a licensed grayscale elevation dataset suitable for the existing Terrain. Read its metadata, report the source and license, then import it at a walkable scale. Preserve the player and unrelated objects.

The normal path is **search → read the source → select an observed data URL → download → import/apply → verify**. A downloaded file is not automatically placed in the scene. For PBR surfaces, use the dedicated [material library](material-library.md).

## Downloads, heightmaps and provenance

Downloads must use a URL actually observed by search or page reading. Supported data includes images, elevation files, supported model/audio files and text metadata; scripts, executables and archives are rejected. Each file is limited to **32 MiB**. Data and source receipts go into a new content-addressed folder under `Assets/WorldWeaverGenerated/Web/`; changed destination files are preserved.

Heightmap import accepts grayscale PNG/TIFF, supported raw 16-bit layouts and detached PDS3 signed big-endian 16-bit elevation with its actual LBL metadata. A colored relief map or a photograph is not scalar height data. Import clones TerrainData and updates its collider; it replaces existing heights/holes, so existing caves need deliberate review. World dimensions are chosen separately from source coverage: shrinking a global map into a small scene is not a geographically accurate reconstruction.

A recorded URL and hash establish provenance, not permission to redistribute. Keep the resource's license and attribution with the asset; the required `license_note` may explicitly record that rights are unresolved. Prefer sources with clear terms.

## Failures and limits

- Search backends can be unavailable or empty. The worker tries bounded alternatives and reports actual attempts; it does not fabricate results.
- `web_research` has a bounded tool loop. A partial investigation can return useful observed sources without completing the research goal.
- The host also limits research time and unchanged repeated failures. Use a different actual source, an existing observation or an offline alternative when the budget is exhausted.
- Reading supports public HTTPS pages, without login, a browser profile, uploads or private-network destinations. Redirects are validated. A site that requires interactive browsing may remain unavailable.
- Page content is reference data. It cannot change the user's request, authorize code execution or grant project-edit permissions.

See [troubleshooting](troubleshooting.md) for worker setup errors and [examples](examples.md) for bounded workflows.

## Open-source components

The worker pins [smolagents](https://github.com/huggingface/smolagents) **1.26.0** (Apache-2.0) and [DDGS](https://github.com/deedy5/ddgs) **9.16.1** (MIT). Search uses DDGS; research uses a real tool-calling agent. Dependency versions are in [requirements.lock](../Packages/com.worldweaver.toolkit/Tools~/web_agent/requirements.lock), and licenses are retained in the package's [third-party notices](../Packages/com.worldweaver.toolkit/ThirdPartyNotices/).

Implementation: [Unity bridge](../Packages/com.worldweaver.toolkit/Editor/Assistant/WebResearchTools.cs), [worker](../Packages/com.worldweaver.toolkit/Tools~/web_agent/bridge.py), [native Ollama model adapter](../Packages/com.worldweaver.toolkit/Tools~/web_agent/ollama_model.py).
