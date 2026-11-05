# Example prompts

These are starting points to edit for your project, **not a list of tested or guaranteed outcomes**. Use the suggested mode, name the target objects and inspect the resulting action records. Interactive checks require the relevant permissions and a working model.

## Inspect before editing · Ask

Select an object in Hierarchy and use **＋ → Прикрепить контекст выделения**:

> Inspect the selected object and its children. Explain their responsibilities and any missing serialized references. Do not change the scene or scripts.

## Review a change · Plan

> Plan a small checkpoint beacon using the existing scene assets. List the objects and scripts you would change, the trigger behavior and how you would check it in Play Mode. Preserve the existing player and lighting.

Review the proposal before choosing **Применить план**.

## Arrange a scene · Execute

> Create a small display area beside the object named Exhibit: a low platform, three pedestals and a camera that frames them. Use existing project materials, keep the current scene objects and do not save the scene yet. Report the created object names.

## Add behavior with C# · Execute

> Add a proximity highlight to the object named Terminal. It should highlight while the player is within range and return to its original appearance afterward. Write the required C#, compile it, attach it and configure references. Preserve unrelated scripts. Check entering and leaving the range in Play Mode and report what was actually checked.

Script import may wait for review unless automatic import/attachment is enabled. Editor C# and runtime probes require their separate permission.

## Build UI · Plan or Execute

> Add a pause menu with Resume and Settings buttons. Keep it usable at different Game view sizes. Configure the event system and button references, then check actual pointer interaction. Explain which behavior is implemented and which still needs work.

## Research an API · Ask

With the [web helper](web-agent.md) installed:

> Find and open official Unity 6 documentation about MaterialPropertyBlock. Explain when it is useful and what limitations matter for this project's render pipeline. Link the pages you actually read. Do not change the project.

## Improve a material · Execute

> Inspect the renderer and render pipeline for the object named CourtyardFloor. Look for a suitable stone material in the project and built-in library first. Before any internet download, report its source and license. Apply a compatible material and capture the actual camera result.

See the [material library](material-library.md) for supported sources.

## Configure an NPC · Plan

> Inspect the NPC named Caretaker. Plan a patrol route and a personality suitable for a cautious guide. Explain how free-text player dialogue and the assigned model are configured. Preserve existing quest references and identify any missing runtime setup.

For runtime model and dialogue settings, see [NPC AI](npc-ai.md).

## Repair a specific failure · Execute

> Inspect the first compiler error in Console. Repair only the generated script responsible for it, preserve other files and run compilation again. Do not mark the task complete until the current source compiles. Report the actual result.

Use a follow-up to describe remaining issues precisely. Prefer “Terminal stays highlighted after the player leaves range” over “everything is broken.”

[Chat workflow](chat-workflow.md) · [Troubleshooting](troubleshooting.md) · [FAQ](faq.md)
