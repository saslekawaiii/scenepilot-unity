# NPC dialogue, memory and social behavior

Unity Editor Agent's runtime NPCs combine a character profile, model-generated responses and validated game actions. A spoken claim is not itself a quest transition, inventory transfer or reward.

## Enable AI on a character

1. Select an NPC with an **`IntelligentNPC`** component.
2. In its Inspector, enable **«Включить ИИ»** and set the name, role, personality, biography, goals and constraints. `Portrait` is optional.
3. Assign **«Модели персонажа»** to an `OrchestratorProfile`. Its `Dialogue` and `Planning` routes reference `ModelProfile` assets with the provider, endpoint, exact model ID and limits.
4. For a hosted API, put only an environment-variable **name** in the model profile. The running game/Editor must receive the credential separately.

You can request this through chat:

> Enable AI for the selected NPC and explicitly assign the model currently selected in chat. Keep its appearance, identity and existing behavior.

The `configure_npc_ai` tool supports `enabled`, `social`, `autonomous`, `topic`, `cooldown_seconds` and **`assign_selected_model`**. Only an explicit `assign_selected_model=true` replaces the NPC's model routes. Selecting another chat model later leaves existing NPC profiles intact.

Use a conversational model that fits the machine and test its actual responses. A newly enabled NPC still needs a working model route; the editor does not invent an AI reply when the provider is unavailable. See [Ollama setup](local-models.md) and [custom APIs](custom-api.md).

## Talk during Play Mode

With the package's player/dialogue setup, approach a character and press **E**. Write a free-form reply and press **Enter**; **Shift+Enter** inserts a line. **«Отменить»** cancels an active response, and **Esc** or **«Закончить»** closes the conversation. Player movement pauses while the dialogue window is open.

The NPC receives its scoped identity, goals, knowledge, recent conversation and validated world context. Test unexpected questions and references to earlier turns; accepting free text does not guarantee that every answer stays factual or in character.

The included letter-quest controls remain separate from free dialogue. Opening a conversation or claiming a task is finished does not bypass item, distance, stage or one-time reward checks. A custom player/UI must provide its own interaction wiring.

## Let NPCs talk to each other

Enable **«Разговаривать с соседями»** in the Inspector, or ask:

> Enable social conversation for the guard and merchant. Let them discuss whether the bridge should close at night: the guard values safety and the merchant values access.

`NpcSocialAutonomy` finds eligible nearby characters and makes alternating real model requests. Chat configuration uses three turns; the component supports up to four. Distinct interests can produce disagreement, but the model's actual answers determine the conversation.

Characters must share a `WorldSaveState`, or the same root when no save-state component exists, and have unique `CharacterId` values. When an enabled `NavMeshAgent` has a baked route, a character can approach a partner. Without a valid path, the system does not claim that the character arrived. Autonomous planning is a separate option from social conversation.

## Memory and request load

- Each NPC keeps bounded scoped memory: up to **32 conversation entries** and **32 episodic memories**. Model context is bounded as well.
- NPC requests use a shared sequential scheduler. A player conversation takes priority over background planning/social requests.
- Background work is limited to **four started router calls per rolling minute**, including repair calls. Provider retries can produce additional HTTP attempts.
- Social conversations use a cooldown, **60 seconds** by default. Increasing NPC count does not make responses instantaneous or provide unlimited simultaneous inference.
- `WorldSaveState` can persist character state, scoped memory, relationships and conversation. Model profiles and secret values are not copied into the save file.

Social dialogue permits only `Talk` and `Wait`. Player-facing actions pass deterministic permission and world-state checks. A rejected structured response can receive one bounded repair attempt; network failures remain explicit errors.

## Integrate another dialogue UI

Use the runtime API around your own input widget:

```csharp
npc.BeginPlayerConversation();
try
{
    PipelineTrace trace = await npc.SpeakToPlayerAsync(playerText, cancellationToken);
    // Inspect trace.Validation for any game action that actually ran.
}
finally
{
    npc.EndPlayerConversation();
}
```

Subscribe to `SpeechReceived` for `NpcUtterance` events and `AiStatusChanged` for request state. If cancellation arrives after a validated action already executed, `ReplySuppressed` can hide the reply while `Validation` retains that action result; do not blindly replay the request. To replace profiles at runtime, finish/cancel active work and call `ReleaseServices()` before the next request.

## Diagnose a quiet or incorrect NPC

| Symptom | Check |
| --- | --- |
| No model response | AI toggle, Dialogue/Planning routes, endpoint, exact ID and inherited credentials. |
| No social conversation | Both characters enabled, unique IDs, same scope, range, cooldown and background budget. |
| NPC does not walk over | Enabled agent, baked NavMesh and an actual complete path. |
| Wrong facts or character voice | Profile constraints, scoped knowledge/memory and the actual selected model's output. |
| Broken mesh or animation | Inspect the imported rig, avatar/controller and visual setup separately from dialogue AI. |

Next: [chat workflow](chat-workflow.md), [examples](examples.md), [troubleshooting](troubleshooting.md).

Implementation: [NPC component](../Packages/com.worldweaver.toolkit/Runtime/NPC/IntelligentNPC.cs), [social controller](../Packages/com.worldweaver.toolkit/Runtime/NPC/NpcSocialAutonomy.cs), [scheduler](../Packages/com.worldweaver.toolkit/Runtime/NPC/NpcInferenceScheduler.cs), [dialogue pipeline](../Packages/com.worldweaver.toolkit/Runtime/Dialogue/NpcPipeline.cs).
