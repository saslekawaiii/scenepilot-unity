# Unity Editor Agent roadmap

The current release is **1.8.1 preview**. Its [validation record](docs/validation/1.8.1/README.md) separates software tests, real model runs and independent observation. An available tool path is not a promise that every model will solve every request.

## Available in 1.8.1

| Area | Implemented path | Important boundary |
| --- | --- | --- |
| Chat workflow | Ask, Plan and Execute; model and power selection. | Permissions come from the user and host settings, not a generated plan. |
| Model connections | Local Ollama discovery and custom API connections. | Quality, context limits, tool support and availability vary by model/provider. |
| Project work | Scene/asset inspection, composition and component/reference operations. | Existing project state and ownership checks constrain changes. |
| C# | Source drafts, compilation, attachment, trusted Editor snippets and reload continuation. | Successful compilation is not gameplay verification; Editor snippets are not a sandbox. |
| Verification | Play jobs, runtime probes, input measurements and rendering/geometry diagnostics. | Checks establish only the properties actually exercised. |
| Recovery | Persistent observations, checkpoints, conflict checks and bounded stall recovery. | Recovery covers supported scene/filesystem changes, not arbitrary external code effects. |
| Research and assets | Optional web researcher, source records, asset import and advisory skill/recipe catalogs. | References do not install extra tool implementations or override permissions. |

## Next priorities

- [ ] Expand preregistered, previously unseen tasks across geometry, UI, animation and multi-scene projects; publish failures and original model attribution.
- [ ] Improve recovery from failed behavioral checks without repeating probes or treating diagnostic reads as implementation.
- [ ] Make progress, active stage, connection limits and remaining work easier to understand during slow requests.
- [ ] Broaden native Editor UI and Windows/Linux acceptance; distinguish interface tests from headless/controller tests.
- [ ] Improve asset matching, rig/animation assessment and visual result evaluation against the user's requested style.
- [ ] Make provider capabilities, local resource needs and usage limits clearer before a long task.
- [ ] Simplify optional web-agent installation and troubleshooting.
- [ ] Add English UI localization while preserving existing projects and serialized data.

## How work is considered complete

A capability needs a usable tool path, permission handling and an observable result. A Markdown skill, a model claim, a compiling script or a passing fixture alone does not close a feature. UI changes need a real interface check; model reliability changes need real requests as well as deterministic regressions.

Keep published packages and historical validation immutable. A later observer can recheck an older artifact, but its success must retain the original model-run version. See [contributing](CONTRIBUTING.md) and [maintaining releases](docs/MAINTAINING.md).
