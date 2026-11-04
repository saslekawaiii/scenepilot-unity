# ScenePilot 1.8 validation

This report separates software tests, actual model requests, and independent checks of the saved results. It is not a general benchmark score.

## Frozen release package

- Unity: **6000.3.27f1**.
- Package source commit: [`4361b1e`](https://github.com/saslekawaiii/scenepilot-unity/commit/4361b1e41d0772d21467d0899981915cd4342636).
- Archive: `com.worldweaver.toolkit-1.8.0.tgz`, byte-for-byte the tested RC23 candidate.
- SHA-256: `d655badfb9c660e6054ad750582a060db1cd0ea49145c2169c5ee0a3f9b6bf09`.
- [1840-file package manifest](package-manifest.json), including individual hashes. The later release commit also contains the public documentation and repository cleanup; the package payload is unchanged.

## Software checks on this exact archive

| Suite | Passed | Failed | Explicitly skipped |
| --- | ---: | ---: | ---: |
| Editor | 824 | 0 | 0 |
| Play Mode | 27 | 0 | 2 |
| Python web tooling | 16 | 0 | 0 |
| Runtime probe acceptance | 11 | 0 | 0 |

The two Play skips are opt-in external model/network tests. They are not counted as successes. Runtime acceptance includes actual frames, keyboard and mouse input, the E interaction edge, a continuous 16-second observation, expected failed assertions, missing assertions, timeout, exception and cancellation. Scenes, files and input devices are checked after cleanup.

[Per-case software results and original local-record hashes](software-tests.json). Unit tests include fixtures; they are not evidence that a real model solved a user request.

## Real model requests and independent read-back

No ready gameplay implementation or solution plan was supplied by the harness. The model used the actual chat controller and tools. Independent observers did not repair the saved game source, scene or mesh. These checks used an available cloud model connection; no paid inference was used.

| Area | Actual model run | Independently checked result |
| --- | --- | --- |
| Warehouse interaction | RC16 continuation | Actual movement, sprint, jump, mouse look, collision, reachability from spawn, five E pickups and final counter. Earlier fresh RC15 attempt failed. |
| UI and light | RC16; checked on RC17 | Slider changes the actual point light; pointer-driven Reset and binding values checked. |
| Relative geometry edits | RC17 | Existing object identities retained, no extra objects, measured transforms; follow-up changes only local X width. |
| Web documentation | RC16 | Official Unity SimpleMove source obtained without scene or project mutations. |
| Lift and UI | RC18; checked on RC21 | Model-authored C#, 8-second motion period, real pointer pause/resume and 17-second continuous observation. |
| Physics repair | RC22 | Actual model repair followed by an independent 30-second observation after initial fall, including final physical support. |

[Versioned model outcomes, request texts, measured scope and limitations](model-requests.json). Earlier short or partial successes are not substituted for later failures. In particular, the RC20 physics result failed at about 18.3 seconds in an extended check; RC22 was repaired by the model and checked again.

The **208 Runtime files** are byte-identical across RC16, RC17, RC18, RC21, RC22 and RC23: [comparison](runtime-equivalence.json). Editor code and model prompts changed between these candidates. Earlier model requests are attributed to their original versions, rather than presented as new RC23 runs.

## Unseen RC23 case that still fails

A new request for an editable hollow octagonal mesh was designed before the first model call. The agent created the mesh, but repeated runtime probes failed. After 31 implementation calls and 5 review calls, the harness cancelled the task because failed checks were followed by repeated reads without a new implementation change.

The independent observer confirmed a physical ring/wall ray miss. The mesh geometry assertions reached before that failure passed, but the overall behavior test is **0/1**. All 86 preserved artifact files were unchanged by the observer. This is an unresolved agent failure, not a successful fresh task or proof of automatic recovery: [full measured scope](hollow-mesh.json).

## Reproduction and limits

Software tests are in the package's `Tests/Editor` and `Tests/PlayMode`; Python tests accompany `Tools~`. Observer and real-controller acceptance sources are under `scripts/`. Run tests in a separate existing consumer with Test Framework enabled; use one owned Editor at a time. The resource guard reuses configured consumers, requires 20 GiB free disk, and bounds stalls. It does not create projects or clone Unity caches.

Original detailed reports and their hashes are retained locally. Public exports omit provider internals, private paths and model reasoning. Reproduction requires the corresponding model access and saved test artifacts; identical model outputs are not guaranteed. GUI inspection in the user's active project is separate from these automated measurements.

These cases do not establish success on any arbitrary request, artistic quality, reliability of every provider, or replacement of a developer. Current limitations and future work are in the [roadmap](../../../ROADMAP.md).
