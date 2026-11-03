using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>
    /// Typed generation contract for scene, gameplay, project and reviewed source operations.
    /// This guides structured decoding; SceneToolExecutor and ScriptDrafts remain authoritative.
    /// Unions are standalone anyOf nodes, without properties/type siblings or remote references.
    /// </summary>
    public static class EditorToolSchema
    {
        private static readonly string[] Kinds =
            { "cube", "sphere", "capsule", "cylinder", "plane", "tree", "rock", "house" };

        /// <summary>Returns a fresh schema for one EditorCommand, suitable for actions.items.</summary>
        public static JObject CommandSchema()
        {
            var commands = new JArray
            {
                Command("create_world", WorldArguments(true), CreationName(), EmptyTarget()),
                Command("update_world", WorldArguments(false), Name(), ExplicitTarget()),
                Command("create_npc", NpcArguments(true), CreationName(), OptionalParent()),
                Command("create_object", ObjectArguments(), CreationName(), OptionalParent()),
                Command("scatter_objects", ScatterArguments(), CreationName(), ExplicitTarget()),
                Command("set_transform", TransformArguments(), Name(), ExplicitTarget()),
                Command("set_material", Object(new JObject
                {
                    ["color"] = Color(), ["smoothness"] = Number(0, 1), ["children"] = Boolean()
                }, "color"), Name(), ExplicitTarget()),
                // Creation and editing are distinct branches so an empty target needs a valid name.
                Command("set_lighting", LightingArguments(), CreationName(), EmptyTarget()),
                Command("set_lighting", LightingArguments(), Name(), ExplicitTarget()),
                Command("create_camera", CameraArguments(), CreationName(), OptionalParent()),
                Command("update_npc", NpcArguments(false), Name(), ExplicitTarget()),
                Command("attach_behavior", BehaviorArguments(), Name(), ExplicitTarget()),
                Command("delete_object", Object(new JObject()), Name(), ExplicitTarget()),
                Command("save_scene", Object(new JObject
                {
                    ["path"] = AssetPath(true, false)
                }), Name(), EmptyTarget()),
                Command("inspect_object", Object(new JObject()), Name(), ExplicitTarget()),
                Command("search_project", Object(new JObject
                {
                    ["query"] = Text(120, 1), ["limit"] = Integer(1, 40)
                }, "query"), Name(), EmptyTarget()),
                Command("read_script", Object(new JObject
                {
                    ["path"] = AssetPath(false, true)
                }, "path"), Name(), EmptyTarget()),
                Command("draft_script", Object(new JObject
                {
                    ["code"] = Text(35000, 1), ["description"] = new JObject { ["type"] = "string" }
                }, "code"), ClassName(), EmptyTarget()),
                Command("attach_script", Object(new JObject()), ClassName(), ExplicitTarget())
            };
            foreach (var schema in WorldGameplayTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in ConstructiveTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in AnimatedCharacterTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in ModelAssetTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in WebResearchTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in WorldMaterialTools.CommandSchemas()) commands.Add(schema);
            foreach (var schema in AgentProjectTools.CommandSchemas()) commands.Add(schema);
            commands.Add(AgentSourcePatches.CommandSchema());
            return new JObject { ["anyOf"] = commands };
        }

        public static JObject Restrict(IEnumerable<string> allowed, bool decoder)
        {
            var set = new HashSet<string>(allowed);
            var schema = CommandSchema();
            schema["anyOf"] = new JArray(((JArray)schema["anyOf"]).OfType<JObject>().Where(c => set.Contains(ToolName(c))));
            if (decoder) RemoveLargeStringBounds(schema);
            return schema;
        }

        public static string ToolName(JObject command) => (string)(command["properties"]?["tool"]?["const"] ?? command["properties"]?["tool"]?["enum"]?[0]);

        /// <summary>
        /// Local-decoder variant of the complete validation schema. Ollama 0.32.6/Qwen3.5:2b
        /// rejected the full nested string bounds with "failed to parse grammar" in a real
        /// think:false probe; removing only maxLength >= 2000 restored content generation.
        /// Keep types, exact tool/argument allowlists, required fields, all shorter bounds,
        /// patterns and array limits. Runtime validators still enforce every original bound.
        /// Related upstream limitation: https://github.com/ggml-org/llama.cpp/issues/25746 .
        /// </summary>
        public static JObject DecoderCommandSchema()
        {
            var schema = (JObject)CommandSchema().DeepClone();
            RemoveLargeStringBounds(schema);
            return schema;
        }

        private static void RemoveLargeStringBounds(JToken node)
        {
            if (node is JObject value)
            {
                if (value["type"]?.Type == JTokenType.String && (string)value["type"] == "string" && value["maxLength"]?.Type == JTokenType.Integer &&
                    (int)value["maxLength"] >= 2000)
                    value.Remove("maxLength");
                foreach (var property in value.Properties()) RemoveLargeStringBounds(property.Value);
            }
            else if (node is JArray array)
                foreach (var child in array) RemoveLargeStringBounds(child);
        }

        private static JObject Command(string tool, JObject args, JObject name, JObject target)
        {
            return Object(new JObject
            {
                ["tool"] = Enum(tool), ["name"] = name, ["target"] = target, ["args"] = args
            }, "tool", "name", "target", "args");
        }

        private static JObject WorldArguments(bool creation)
        {
            // All TerrainSpec members are optional here: WorldComposer merges partial args with defaults.
            // llama.cpp emits declared optional keys in schema order. A model that
            // starts with style must still be able to emit the dimensions after it.
            var terrain = TerrainFields();
            var properties = new JObject { ["style"] = Enum("forest", "alpine", "desert", "moon") };
            foreach (string key in new[] { "width", "length", "height" })
            { properties[key] = terrain[key]; terrain.Remove(key); }
            if (creation)
            {
                properties["trees"] = Integer(0, 300);
                properties["buildings"] = Integer(0, 100);
                properties["rocks"] = Integer(0, 300);
            }
            foreach (var property in terrain.Properties()) properties[property.Name] = property.Value;
            return Object(properties);
        }

        /// <summary>Order hints for local constrained decoding; JSON/runtime validity remains order-independent.</summary>
        public static string DecoderArgumentOrderHint()
        {
            var lines = new List<string>();
            foreach (JObject command in (JArray)CommandSchema()["anyOf"])
            {
                var args = (JObject)command["properties"]["args"];
                var branches = args["anyOf"] as JArray ?? new JArray(args);
                foreach (JObject branch in branches)
                {
                    var properties = (JObject)branch["properties"];
                    var required = (branch["required"] as JArray ?? new JArray()).Values<string>().ToArray();
                    string tool = ToolName(command);
                    if (properties["behavior"]?["enum"] is JArray modes) tool += "(" + string.Join("/", modes.Values<string>()) + ")";
                    string line = tool + ": " + string.Join(",", required.Concat(properties.Properties().Select(p => p.Name).Where(key => !required.Contains(key))));
                    if (!lines.Contains(line)) lines.Add(line);
                }
            }
            return "LOCAL DECODER KEY ORDER: required keys come first, then optional keys in the order below. Skip unwanted optional keys, but never try to return to an earlier key after a later one. Plan keys: reply,complete,actions. Command keys: tool,name,target,args. Argument orders:\n" + string.Join("\n", lines);
        }

        private static JObject TerrainFields()
        {
            return new JObject
            {
                ["seed"] = Integer(int.MinValue, int.MaxValue),
                ["width"] = Number(32, 2048), ["length"] = Number(32, 2048),
                ["height"] = Number(4, 512),
                ["resolution"] = new JObject
                    { ["type"] = "integer", ["enum"] = new JArray(33, 65, 129, 257, 513) },
                // Terrain noise scale is a scalar; an object's transform scale is a three-vector.
                ["scale"] = Number(.1, 32), ["octaves"] = Integer(1, 8),
                ["persistence"] = Number(.1, .9), ["lacunarity"] = Number(1, 4),
                ["mountainStrength"] = Number(0, 1), ["valleyDepth"] = Number(0, .8),
                ["waterLevel"] = Number(0, .8), ["southSlope"] = Number(0, 1),
                ["smoothingPasses"] = Integer(0, 8)
            };
        }

        private static JObject NpcArguments(bool creation)
        {
            var properties = new JObject
            {
                ["role"] = Text(4000), ["personality"] = Text(4000),
                ["biography"] = Text(4000), ["goals"] = Text(4000),
                ["constraints"] = Text(4000), ["knowledge"] = Knowledge()
            };
            if (creation) AddTransformFields(properties, true);
            properties["inventory"] = Inventory(); properties["autonomous"] = Boolean();
            return Object(properties);
        }

        private static JObject ObjectArguments()
        {
            var properties = new JObject
            {
                ["kind"] = Enum(Kinds)
            };
            AddTransformFields(properties, false);
            properties["color"] = Color(); properties["on_terrain"] = Boolean(); properties["inventory"] = Inventory();
            return Object(properties, "kind");
        }

        private static JObject ScatterArguments()
        {
            // The validator intentionally defaults count to one; only kind is required in args.
            // The parent terrain/world is always required in the command's target property.
            return Object(new JObject
            {
                ["kind"] = Enum(Kinds), ["count"] = Integer(1, 300),
                ["seed"] = Integer(int.MinValue, int.MaxValue), ["center"] = Vector(),
                ["size"] = Array(Number(1, 2048), 2, 2), ["spacing"] = Number(.1, 100)
            }, "kind");
        }

        private static JObject TransformArguments()
        {
            // Express the nonempty transform requirement in grammar-friendly independent branches.
            var variants = new JArray();
            foreach (var required in new[] { "position", "rotation", "scale" })
            {
                var properties = new JObject();
                AddTransformFields(properties, false);
                variants.Add(Object(properties, required));
            }
            return new JObject { ["anyOf"] = variants };
        }

        private static void AddTransformFields(JObject properties, bool onTerrain)
        {
            properties["position"] = Vector();
            properties["rotation"] = Vector();
            properties["scale"] = Vector(.01, 100);
            if (onTerrain) properties["on_terrain"] = Boolean();
        }

        private static JObject LightingArguments()
        {
            return Object(new JObject
            {
                ["type"] = Enum("directional", "point", "spot"),
                ["intensity"] = Number(0, 8), ["color"] = Color(),
                ["position"] = Vector(), ["rotation"] = Vector(),
                ["range"] = Number(.1, 500), ["spot_angle"] = Number(1, 179),
                ["shadows"] = Boolean(), ["on_terrain"] = Boolean()
            });
        }

        private static JObject CameraArguments()
        {
            return Object(new JObject
            {
                ["position"] = Vector(), ["rotation"] = Vector(), ["look_at"] = Vector(),
                ["field_of_view"] = Number(10, 120), ["orthographic"] = Boolean(),
                ["size"] = Number(1, 1000), ["enabled"] = Boolean(), ["background"] = Color()
            });
        }

        private static JObject BehaviorArguments()
        {
            var variants = new JArray();
            foreach (var behavior in new[] { "rotate", "bob", "patrol", "followplayer" })
            {
                var properties = new JObject
                {
                    ["behavior"] = Enum(behavior), ["speed"] = Number(.01, 360),
                    ["amplitude"] = Number(0, 10), ["frequency"] = Number(.01, 10),
                    ["axis"] = Vector(-1, 1), ["points"] = Array(Vector(), 2, 32),
                    ["follow_target"] = ExplicitTarget(), ["stopping_distance"] = Number(0, 20)
                };
                var required = behavior == "patrol" ? new[] { "behavior", "points" }
                    : behavior == "followplayer" ? new[] { "behavior", "follow_target" }
                    : new[] { "behavior" };
                variants.Add(Object(properties, required));
            }
            return new JObject { ["anyOf"] = variants };
        }

        private static JObject Knowledge()
        {
            // The aggregate 16000-character budget across the array is checked at runtime.
            return new JObject { ["anyOf"] = new JArray(Text(16000), Array(Text(4000), 0, 32)) };
        }

        private static JObject Inventory()
        {
            // Item IDs are dynamic keys, the only map whose additional properties are allowed.
            // Runtime also rejects whitespace-only IDs and a quantity sum greater than 10000.
            return new JObject
            {
                ["type"] = "object", ["properties"] = new JObject(),
                ["propertyNames"] = Text(80, 1), ["maxProperties"] = 30,
                ["additionalProperties"] = Integer(0, 10000)
            };
        }

        private static JObject Color()
        {
            return new JObject
            {
                ["anyOf"] = new JArray
                {
                    new JObject { ["type"] = "string", ["pattern"] = "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$" },
                    Array(Number(0, 1), 3, 4)
                }
            };
        }

        private static JObject AssetPath(bool generated, bool script)
        {
            var result = Text(240, 1);
            result["pattern"] = generated
                ? "^Assets/WorldWeaverGenerated/.+[.][uU][nN][iI][tT][yY]$"
                : "^(Assets|Packages)/.+[.][cC][sS]$";
            result["description"] = script
                ? "Existing readable C# asset under Assets or an installed Packages folder; no traversal, backslashes or forbidden path characters. Package source is read-only."
                : "New scene destination under Assets/WorldWeaverGenerated; never overwrites an existing scene.";
            return result;
        }

        private static JObject Name() => Text(160);
        private static JObject CreationName()
        {
            var result = Text(160);
            result["description"] = "Optional unique scene name; empty gets an automatic unique default. Explicit names cannot contain control characters, slash or backslash.";
            return result;
        }
        private static JObject ClassName()
        {
            return new JObject
            {
                ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 80,
                ["pattern"] = "^[A-Za-z_][A-Za-z0-9_]{0,79}$",
                ["description"] = "REQUIRED exact compiled/drafted C# class name, e.g. MouseLookCamera. Not a path; never omit this field."
            };
        }
        private static JObject EmptyTarget() => Enum("");
        private static JObject OptionalParent() => Text(200);
        private static JObject ExplicitTarget()
        {
            var result = Text(200, 1);
            result["description"] = "Existing active-scene target: id:<integer>, selected, unique name or @earlier-created-name. Never id:@name.";
            return result;
        }
        private static JObject Vector(double min = -100000, double max = 100000) => Array(Number(min, max), 3, 3);
        private static JObject Boolean() => new JObject { ["type"] = "boolean" };
        private static JObject Text(int max, int min = 0)
            => new JObject { ["type"] = "string", ["minLength"] = min, ["maxLength"] = max };
        private static JObject Enum(params string[] values)
            => new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        private static JObject Number(double min, double max)
            => new JObject { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
        private static JObject Integer(int min, int max)
            => new JObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
        private static JObject Array(JObject items, int min, int max)
            => new JObject { ["type"] = "array", ["items"] = items, ["minItems"] = min, ["maxItems"] = max };
        private static JObject Object(JObject properties, params string[] required)
        {
            return new JObject
            {
                ["type"] = "object", ["properties"] = properties,
                ["additionalProperties"] = false, ["required"] = new JArray(required)
            };
        }
    }
}
