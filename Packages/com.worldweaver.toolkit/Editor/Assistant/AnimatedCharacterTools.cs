using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Creates an actual imported and animated character without asking an LLM to emit mesh or rig code.</summary>
    internal static class AnimatedCharacterTools
    {
        internal const string ToolName = "create_animated_player";
        internal const string SourceFolder = "Packages/com.worldweaver.toolkit/CharacterAssets/KenneyMiniCharacters/";
        internal const string ToolReference = "create_animated_player: create or reuse a playable main character. For an imported realistic character pass args {model_path:actual Assets/ or Packages/ FBX/prefab path,height?:.1..20,controller_path?:existing compatible controller,position?:[x,y,z]}; use model_search/model_import first. Creates controls/camera directly with the chosen model; no stylized intermediate actor is needed. Alternatively args {preset?:adventurer|explorer,position?:[x,y,z]} use Kenney CC0 stylized models with authored clips. Choose model_path OR preset. Target an existing owned world/player or empty for a standalone player, even in a completely empty scene. create_player is a different primitive/world tool and requires a world target. Imported Humanoids without a controller use explicitly identified procedural bone locomotion. Reuses an existing player, preserves its controls and unrelated objects. No arbitrary download URLs.";
        internal static bool Handles(string tool) => tool == ToolName;
        internal static bool IsSourceModelPath(string path) => path == SourceFolder + "adventurer.fbx" || path == SourceFolder + "explorer.fbx";
        internal static string CanonicalClipName(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            int separator = Math.Max(name.LastIndexOf('|'), name.LastIndexOf(':'));
            return separator < 0 ? name : name.Substring(separator + 1).Trim();
        }

        internal static IEnumerable<JObject> CommandSchemas()
        {
            yield return new JObject {
                ["type"]="object", ["additionalProperties"]=false,
                ["properties"]=new JObject {
                    ["tool"]=new JObject { ["type"]="string", ["const"]=ToolName },
                    ["name"]=new JObject { ["type"]="string" }, ["target"]=new JObject { ["type"]="string" },
                    ["args"]=new JObject { ["type"]="object", ["additionalProperties"]=false,
                        ["properties"]=new JObject {
                            ["model_path"]=new JObject{["type"]="string",["minLength"]=1,["maxLength"]=240},
                            ["height"]=new JObject{["type"]="number",["minimum"]=.1,["maximum"]=20},
                            ["controller_path"]=new JObject{["type"]="string",["minLength"]=1,["maxLength"]=240},
                            ["preset"]=new JObject { ["type"]="string", ["enum"]=new JArray("adventurer","explorer") },
                            ["position"]=new JObject { ["type"]="array", ["minItems"]=3, ["maxItems"]=3,
                                ["items"]=new JObject { ["type"]="number", ["minimum"]=-100000, ["maximum"]=100000 } }
                        }, ["required"]=new JArray() }
                }, ["required"]=new JArray("tool","name","target","args")
            };
        }

        private static string Preset(EditorCommand command) => (string)command.args?["preset"] ?? "adventurer";
        private static GameObject Model(string preset)
        {
            if (preset != "adventurer" && preset != "explorer") throw new ArgumentException("Unknown built-in character preset: " + preset);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFolder + preset + ".fbx");
            if (!model || model.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => !r.sharedMesh || r.bones.Length < 4))
                throw new InvalidOperationException("Built-in animated character is unavailable or has no imported skinned rig. Reimport the package; no primitive fallback is used.");
            var clips = Clips(preset);
            foreach (string required in new[] { "idle", "walk", "sprint", "jump", "fall" })
                if (!clips.TryGetValue(required, out var clip) || clip.length <= 0)
                    throw new InvalidOperationException("Built-in character is missing its authored animation: " + required);
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(SourceFolder + "colormap.png")) throw new InvalidOperationException("Built-in character texture is missing.");
            return model;
        }
        private static Dictionary<string, AnimationClip> Clips(string preset) => AssetDatabase.LoadAllAssetsAtPath(SourceFolder + preset + ".fbx")
            .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).GroupBy(c => CanonicalClipName(c.name)).ToDictionary(g => g.Key, g => g.First());

        internal static void Validate(EditorCommand command, Func<string, GameObject> resolve)
        {
            if (command == null || command.tool != ToolName || command.args == null) throw new ArgumentException("Invalid animated character command.");
            if (command.args.Properties().Any(p => p.Name != "preset" && p.Name != "position" && p.Name!="model_path" && p.Name!="controller_path" && p.Name!="height")) throw new ArgumentException("Unknown animated character argument.");
            if (command.args["preset"] != null && command.args["preset"].Type != JTokenType.String) throw new ArgumentException("Character preset must be a string.");
            if (command.args["position"] != null)
            {
                if (!(command.args["position"] is JArray position) || position.Count != 3) throw new ArgumentException("Character position must contain three finite coordinates.");
                foreach (var value in position)
                {
                    if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) throw new ArgumentException("Character coordinates must be numeric.");
                    double number = (double)value;
                    if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 100000) throw new ArgumentException("Character coordinates are outside the supported range.");
                }
            }
            if(command.args["model_path"]!=null)
            {
                if(command.args["model_path"].Type!=JTokenType.String||command.args["preset"]!=null)throw new ArgumentException("Choose one model_path or preset.");
                var source=ModelAssetTools.ProjectModel((string)command.args["model_path"]);
                if(source.GetComponentsInChildren<MonoBehaviour>(true).Any()||!source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.sharedMesh&&r.bones.Length>=4))throw new ArgumentException("Choose a skinned data-only character model.");
                if(command.args["height"]!=null&&((command.args["height"].Type!=JTokenType.Integer&&command.args["height"].Type!=JTokenType.Float)||(double)command.args["height"]<.1||(double)command.args["height"]>20))throw new ArgumentException("Invalid model height.");
            }
            else Model(Preset(command));
            if (string.IsNullOrEmpty(command.target) && Object.FindObjectsByType<WorldPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(p => p.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()))
                throw new ArgumentException("The scene already has a player. Target its generated world or the existing player to upgrade its appearance without creating a duplicate.");
            if (!string.IsNullOrEmpty(command.target))
            {
                var target = resolve(command.target);
                if (!target || !target.GetComponent<GeneratedSceneObject>()) throw new ArgumentException("Animated character target must be an existing generated world or player.");
                var players = target.GetComponentsInChildren<WorldPlayer>(true);
                if (players.Length > 1) throw new ArgumentException("The target contains several players. Choose a single player explicitly.");
                if (players.Length == 1 && !players[0].GetComponent<GeneratedSceneObject>()) throw new ArgumentException("The existing player is not owned by WorldWeaver.");
            }
        }
        internal static int EstimateCost(EditorCommand command) => (command.args["model_path"]==null?Model(Preset(command)):ModelAssetTools.ProjectModel((string)command.args["model_path"])).GetComponentsInChildren<Transform>(true).Length + 5;

        internal static WorldGameplayToolResult Execute(EditorCommand command, SceneBuildTransaction tx, Transform parent,
            Func<string, GameObject> resolve, OrchestratorProfile routes)
        {
            Validate(command, resolve);
            GameObject target = string.IsNullOrEmpty(command.target) ? null : resolve(command.target);
            Transform container = target ? target.transform : parent;
            var player = target ? target.GetComponentInChildren<WorldPlayer>(true) : null;
            bool reused = player;
            GameObject go;
            if (player) go = player.gameObject;
            else
            {
                go = tx.Empty(string.IsNullOrWhiteSpace(command.name) ? "Главный герой" : command.name, container, "player");
                var motor = Undo.AddComponent<CharacterController>(go);
                motor.height = 1.8f; motor.radius = .3f; motor.center = new Vector3(0, .9f, 0); motor.stepOffset = .3f;
                Undo.AddComponent<Inventory>(go);
                player = Undo.AddComponent<WorldPlayer>(go);
                player.AlwaysLook=true;
                player.LastMessage="WASD — движение · Shift — бег · Space — прыжок · мышь — обзор · E — разговор";
                var place = Undo.AddComponent<WorldPlace>(go); place.Key = "player_spawn"; place.Kind = WorldPlaceKind.Spawn;
                var entity = Undo.AddComponent<WorldEntity>(go); entity.Id = place.PersistentId;
            }
            Undo.RecordObject(player, "Configure walking and sprinting character");
            player.Speed = 2.2f; player.SprintMultiplier = 2.5f;
            var collision=go.GetComponent<CharacterController>();Undo.RecordObject(collision,"Preserve small character movements");collision.minMoveDistance=0;
            Undo.RecordObject(go.transform, "Place animated player");
            if (command.args["position"] is JArray position) go.transform.position = new Vector3((float)position[0], (float)position[1], (float)position[2]);
            else if (!reused)
            {
                go.transform.position = InitialSpawn(container);
            }
            // Existing root primitive appearance belongs to the generated player. Preserve the motor,
            // inventory, camera, references and unrelated child objects while removing the placeholder.
            var oldRootMesh = go.GetComponent<MeshFilter>(); var oldRootRenderer = go.GetComponent<MeshRenderer>();
            if (oldRootMesh && oldRootMesh.sharedMesh && oldRootRenderer &&
                new[] { "Capsule", "Cube", "Sphere", "Cylinder", "Plane", "Quad" }.Contains(oldRootMesh.sharedMesh.name) &&
                !AssetDatabase.GetAssetPath(oldRootMesh.sharedMesh).StartsWith("Assets/", StringComparison.Ordinal) &&
                !AssetDatabase.GetAssetPath(oldRootMesh.sharedMesh).StartsWith("Packages/", StringComparison.Ordinal))
            { Undo.RecordObject(oldRootRenderer, "Hide generated primitive placeholder"); oldRootRenderer.enabled = false; }
            bool previewFloor = EnsureGround(tx, go, target && target.GetComponent<WorldPlayer>() ? target.transform.parent : container);
            var binder = go.GetComponent<WorldCharacterAnimation>() ?? Undo.AddComponent<WorldCharacterAnimation>(go);
            Undo.RecordObject(binder, "Configure animated character");
            JObject customReport=null;
            if(command.args["model_path"]!=null)
            {
                var arguments=new JObject{["model_path"]=command.args["model_path"].DeepClone(),["height"]=(float?)command.args["height"]??1.8f};
                if(command.args["controller_path"]!=null)arguments["controller_path"]=command.args["controller_path"].DeepClone();
                var bound=ModelAssetTools.Execute(new EditorCommand{tool="replace_model",target=ProjectContextIndex.StableTarget(go),args=arguments},_=>go,tx);customReport=JObject.Parse(bound.Summary);
            }
            else
            {
            if (binder.Visual) Undo.DestroyObjectImmediate(binder.Visual.gameObject);
            string preset = Preset(command);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(Model(preset), go.scene);
            if (!visual) throw new InvalidOperationException("Cannot instantiate the imported character.");
            Undo.RegisterCreatedObjectUndo(visual, "Import animated character");
            foreach(var node in visual.GetComponentsInChildren<Transform>(true))
            {
                var owned=node.GetComponent<GeneratedSceneObject>()??Undo.AddComponent<GeneratedSceneObject>(node.gameObject);
                owned.Kind=node==visual.transform?"character_model":"part";owned.BatchId=tx.BatchId;owned.EnsureUniqueIdentity();
                tx.Objects.Add(node.gameObject);
            }
            visual.name = "Облик · " + preset; visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity;
            // Use imported mesh bounds to fit the actual skinned model to the collision capsule.
            var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.y <= .01f || float.IsNaN(bounds.size.y) || float.IsInfinity(bounds.size.y)) throw new InvalidOperationException("The imported character has invalid bounds.");
            visual.transform.localScale *= 1.8f / bounds.size.y;
            bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            visual.transform.position += Vector3.up * (go.transform.position.y - bounds.min.y);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceFolder + "colormap.png");
            var material = tx.Material("Character skin " + preset, Color.white, .12f);
            material.mainTexture = texture; if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            foreach (var renderer in renderers) renderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, renderer.sharedMesh.subMeshCount)).ToArray();
            var animator = visual.GetComponent<Animator>() ?? Undo.AddComponent<Animator>(visual);
            Undo.RecordObject(animator, "Set authored character animation");
            animator.runtimeAnimatorController = CreateController(tx, preset);
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            binder.Animator = animator; binder.Motor = go.GetComponent<CharacterController>(); binder.Visual = visual.transform;
            var holder=go.GetComponent<WorldModelVisual>();if(holder){Undo.RecordObject(holder,"Track switched character preset");holder.Visual=visual.transform;holder.SourcePath=SourceFolder+preset+".fbx";}
            }
            if (!player.View)
            {
                var camera = tx.Empty("Камера героя", go.transform, "camera");
                camera.transform.localPosition = new Vector3(0, 2.4f, -4.2f); camera.transform.localRotation = Quaternion.Euler(18, 0, 0);
                var view = Undo.AddComponent<Camera>(camera); view.depth = 100; view.nearClipPlane = .1f; view.farClipPlane = 2000; view.enabled = false;
                Undo.RecordObject(player, "Assign character camera"); player.View = view;
            }
            EditorUtility.SetDirty(player); EditorUtility.SetDirty(binder);
            var report = customReport??Inspect(go); if(customReport==null)report["preset"] = Preset(command);else report["success"]=player&&player.View&&binder.Visual&&binder.Animator; report["reusedExistingPlayer"] = reused;
            report["createdPreviewFloor"] = previewFloor;
            if (previewFloor) report["previewScope"] = "A small 16 × 16 m walkable presentation stage was added because no suitable physical floor existed at the spawn. This is not a generated full game world.";
            report["source"] = customReport==null?"Kenney Mini Characters 1 (CC0-1.0)":(string)command.args["model_path"]; report["controls"] = "WASD · Shift — бег · Space — прыжок · "+(player.AlwaysLook?"мышь — обзор":"правая кнопка мыши — камера")+" · E — разговор";
            if (!(bool)report["success"]) throw new InvalidOperationException("Animated character read-back failed: " + report);
            return new WorldGameplayToolResult { Target = go, Summary = report.ToString(Newtonsoft.Json.Formatting.None) };
        }

        private static Vector3 InitialSpawn(Transform container)
        {
            Vector3 origin = container ? container.position : Vector3.zero;
            var terrain = container ? container.GetComponentInChildren<Terrain>() : null;
            if (!terrain) return origin;
            var data = terrain.terrainData; var start = terrain.transform.position;
            var spec = terrain.GetComponent<GeneratedTerrain>();
            float water = spec ? start.y + spec.Specification.waterLevel * data.size.y : float.NegativeInfinity;
            Vector3 best = start + new Vector3(data.size.x * .5f, 0, data.size.z * .5f);
            float bestDistance = float.PositiveInfinity;
            for (int z = 1; z <= 9; z++) for (int x = 1; x <= 9; x++)
            {
                Vector3 candidate = start + new Vector3(data.size.x * x / 10, 0, data.size.z * z / 10);
                float height = terrain.SampleHeight(candidate) + start.y;
                if (height <= water + .1f) continue;
                float distance = new Vector2(candidate.x - origin.x, candidate.z - origin.z).sqrMagnitude;
                if (distance >= bestDistance) continue;
                candidate.y = height + .05f; best = candidate; bestDistance = distance;
            }
            if (float.IsPositiveInfinity(bestDistance)) best.y = Mathf.Max(terrain.SampleHeight(best) + start.y, water) + .1f;
            return best;
        }

        private static bool EnsureGround(SceneBuildTransaction tx, GameObject hero, Transform parent)
        {
            Physics.SyncTransforms();
            Vector3 origin = hero.transform.position + Vector3.up * .6f;
            var terrain = parent ? parent.GetComponentInChildren<Terrain>() : null;
            var generatedTerrain = terrain ? terrain.GetComponent<GeneratedTerrain>() : null;
            float water = generatedTerrain ? terrain.transform.position.y + generatedTerrain.Specification.waterLevel * terrain.terrainData.size.y : float.NegativeInfinity;
            bool hasGround = Physics.RaycastAll(origin, Vector3.down, 100, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider && !hit.collider.transform.IsChildOf(hero.transform) && hit.point.y >= water - .01f);
            if (hasGround) return false;
            var stage = tx.Empty("Площадка просмотра персонажа", parent, "character_preview_floor");
            stage.transform.position = hero.transform.position;
            // A separate hand-authored floor mesh is not used as the character's visible geometry.
            var mesh = tx.Asset(new Mesh { name = "Character presentation floor",
                vertices = new[] { new Vector3(-8,0,-8), new Vector3(-8,0,8), new Vector3(8,0,8), new Vector3(8,0,-8) },
                triangles = new[] { 0,1,2,0,2,3 }, uv = new[] { Vector2.zero, new Vector2(0,4), new Vector2(4,4), new Vector2(4,0) } }, "Character presentation floor");
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Undo.AddComponent<MeshFilter>(stage).sharedMesh = mesh;
            Undo.AddComponent<MeshRenderer>(stage).sharedMaterial = tx.Material("Character preview stone", new Color(.28f,.32f,.34f));
            Undo.AddComponent<MeshCollider>(stage).sharedMesh = mesh;
            if (!Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l => l.enabled && l.gameObject.scene == hero.scene))
            {
                var lamp = tx.Empty("Свет просмотра персонажа", stage.transform, "light");
                lamp.transform.rotation = Quaternion.Euler(45, -35, 0);
                var light = Undo.AddComponent<Light>(lamp); light.type = LightType.Directional; light.intensity = 1.2f; light.shadows = LightShadows.Soft;
            }
            Physics.SyncTransforms();
            return true;
        }

        private static AnimatorController CreateController(SceneBuildTransaction tx, string preset)
        {
            var controller = tx.Asset(new AnimatorController { name = "Locomotion " + preset }, "Character locomotion " + preset, ".controller");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddLayer("Locomotion");
            var machine = controller.layers[0].stateMachine;
            var clips = Clips(preset);
            var locomotion = machine.AddState("Idle Walk Run");
            var tree = new BlendTree { name = "Authored locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["idle"], 0); tree.AddChild(clips["walk"], 2.2f); tree.AddChild(clips["sprint"], 5.5f);
            locomotion.motion = tree; machine.defaultState = locomotion;
            var jump = machine.AddState("Jump"); jump.motion = clips["jump"];
            var fall = machine.AddState("Fall"); fall.motion = clips["fall"];
            var startJump = locomotion.AddTransition(jump); startJump.hasExitTime = false; startJump.duration = .12f; startJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            var startFall = jump.AddTransition(fall); startFall.hasExitTime = true; startFall.exitTime = .8f; startFall.duration = .1f;
            foreach (var airborne in new[] { jump, fall })
            { var land = airborne.AddTransition(locomotion); land.hasExitTime = false; land.duration = .12f; land.AddCondition(AnimatorConditionMode.If, 0, "Grounded"); }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        internal static JObject Inspect(GameObject go)
        {
            var binder = go ? go.GetComponent<WorldCharacterAnimation>() : null;
            var animator = binder ? binder.Animator : null;
            var renderers = binder && binder.Visual ? binder.Visual.GetComponentsInChildren<SkinnedMeshRenderer>(true) : Array.Empty<SkinnedMeshRenderer>();
            var clips = animator && animator.runtimeAnimatorController ? animator.runtimeAnimatorController.animationClips : Array.Empty<AnimationClip>();
            var names = clips.Where(c => c).Select(c => CanonicalClipName(c.name)).Distinct().ToArray();
            var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
            var machine = controller && controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
            var tree = machine && machine.defaultState ? machine.defaultState.motion as BlendTree : null;
            bool connected = tree && tree.blendParameter == "Speed" && tree.children.Length == 3 &&
                new[] { "idle", "walk", "sprint" }.All(n => tree.children.Any(c => c.motion is AnimationClip clip && CanonicalClipName(clip.name) == n));
            var checks = new JObject {
                ["worldPlayer"] = go && go.GetComponent<WorldPlayer>(),
                ["characterController"] = go && go.GetComponent<CharacterController>() && go.GetComponent<CharacterController>().enabled,
                ["smallMovementSteps"] = go && go.GetComponent<CharacterController>() && go.GetComponent<CharacterController>().minMoveDistance==0,
                ["camera"] = go && go.GetComponent<WorldPlayer>() && go.GetComponent<WorldPlayer>().View,
                ["animationBinding"] = binder && binder.Animator && binder.Motor && binder.Visual,
                ["importedSkinnedMesh"] = renderers.Any(r => r.sharedMesh && r.sharedMesh.vertexCount > 100 && r.bones.Length >= 4 && r.bones.All(b => b)),
                ["texturedMesh"] = renderers.Length > 0 && renderers.All(r => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m && m.mainTexture)),
                ["animatorController"] = animator && animator.runtimeAnimatorController && !animator.applyRootMotion,
                ["connectedMovementBlendTree"] = connected,
                ["authoredIdleWalkRun"] = new[] { "idle", "walk", "sprint" }.All(n => names.Contains(n)),
                ["authoredJumpFall"] = new[] { "jump", "fall" }.All(n => names.Contains(n))
            };
            return new JObject { ["success"] = checks.Properties().All(p => (bool)p.Value), ["checks"] = checks,
                ["skinnedRenderers"] = renderers.Length, ["vertices"] = renderers.Where(r => r.sharedMesh).Sum(r => r.sharedMesh.vertexCount),
                ["bones"] = renderers.SelectMany(r => r.bones).Where(b => b).Distinct().Count(), ["clips"] = new JArray(names),
                ["rig"] = "Generic (original 7-bone source skeleton); no Humanoid retargeting claim" };
        }
    }
}
