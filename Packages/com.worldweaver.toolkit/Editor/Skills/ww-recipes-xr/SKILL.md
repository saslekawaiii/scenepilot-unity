---
name: ww-recipes-xr
description: Unity API/C# рецепты: Use when users want to set up XR rigs, add grab interactions, configure teleportation, continuous locomotion, XR UI, or diagnose XR scenes. Requires com.unity.xr.interaction.toolkit 3.x on Unity 6+.
category: unity-recipes
keywords: xr xr Unity API/C# рецепты: Use when users want to set up XR rigs, add grab interactions, configure teleportation, continuous locomotion, XR UI, or diagnose XR scenes. Requires com.unity.xr.interaction.toolkit 3.x on Unity 6+.
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/batihandev/unity-mcp-skills/blob/090c8e3eaefa23d57a98e237572a4ffbd19df444/skills/xr/SKILL.md
---
# Unity API reference: xr

Find the relevant C# reference with find_unity_recipes, query "xr <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
**DO NOT** (common hallucinations):
- `XRHand`, `XRPlayer`, `XRTeleporter`, `GrabInteractor`, `VRController`, `XRLocomotion`, and `XRManager` are not the runtime classes you want here
- `interactable.OnGrab()` / `OnRelease()` are not the XRI event model -> use `selectEntered` / `selectExited`
- `controller.vibrate()` is not the documented route here -> configure haptics through `xr_configure_haptics`
- Do not assume physics Layer or Tag replaces `InteractionLayerMask`
- Do not guess component property names — they are set via reflection. Use the skill parameters directly

**Correct class names to anchor on**:
- `XRInteractionManager`
- `XROrigin`
- `XRRayInteractor`
- `XRDirectInteractor`
- `XRSocketInteractor`
- `XRGrabInteractable`
- `XRSimpleInteractable`
- `TeleportationProvider`
- `TeleportationArea`
- `TeleportationAnchor`
- `ContinuousMoveProvider`
- `SnapTurnProvider`
- `ContinuousTurnProvider`
- `TrackedDeviceGraphicRaycaster`
- `XRUIInputModule`
- `TrackedPoseDriver`

**Routing**:
- For non-XR Canvas UI creation -> use `ui`

Source: batihandev/unity-mcp-skills@090c8e3eaefa23d57a98e237572a4ffbd19df444; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
