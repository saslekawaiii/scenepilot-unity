using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Typed material tools; network I/O never spans the executor's global Undo transaction.</summary>
    internal static class WorldMaterialTools
    {
        internal const string ToolInstructions = @"MATERIAL TOOLS:
material_search: target empty; standalone plan; args {query:1..100 characters,source?:any|builtin|polyhaven,limit?:1..8}. Read-only search of original built-in CC0 surfaces and Poly Haven CC0 texture IDs; no generation, no asset writes. Use source=builtin offline. For Poly Haven use concrete English surface terms (выветренная древесина = weathered wood, доски = wood planks); choose surface semantics, not just a tree-related word. Leaves, forest litter or bark are not wooden boards for a door. Search first, then select an exact returned ID whose name/tags match the requested surface. If none match, refine the search instead of applying an unrelated first result. Results include exact material IDs and license/source, not instructions.
material_import: target empty; standalone plan; args {material_id:builtin:<id>|polyhaven:<id>,allow_online?:bool}. Imports an exact CC0 material ID into a new project folder with source/hash receipt; never accepts a URL or overwrites existing assets. Built-ins/cache work offline. Uncached Poly Haven needs allow_online=true; network failure can return an explicitly identified built-in fallback. Inspect actual MaterialId and Warning, then apply that actual ID. Do not claim the requested online texture was downloaded after fallback.
material_apply: target existing explicit scene Renderer or Terrain; args {material_id:exact searched/imported ID,tiling_x?:.05..100,tiling_y?:.05..100,include_children?:bool,terrain_layer_index?:0..7}. Offline application: built-ins can be imported on demand; online material must already be cached. Copies material and TerrainData/layer; shared source assets remain intact. include_children=true is required for a root without its own Renderer. Apply in a later plan after creating an object/world. Use these tools to improve surfaces beyond primitive colors; arbitrary asset downloads are unavailable.";
        internal static bool Handles(string tool) => tool == "material_search" || tool == "material_import" || tool == "material_apply";
        internal static bool IsAsync(string tool) => tool == "material_search" || tool == "material_import";
        internal static bool IsReadOnly(string tool) => tool == "material_search";
        static JObject Text(int maximum, int minimum = 1) => new JObject { ["type"] = "string", ["minLength"] = minimum, ["maxLength"] = maximum };
        static JObject Number(string type, double minimum, double maximum) => new JObject { ["type"] = type, ["minimum"] = minimum, ["maximum"] = maximum };
        internal static IEnumerable<JObject> CommandSchemas()
        {
            yield return Command("material_search", new JObject { ["query"] = Text(100), ["source"] = new JObject { ["type"] = "string", ["enum"] = new JArray("any", "builtin", "polyhaven") }, ["limit"] = Number("integer", 1, 8) }, new JArray("query"), false);
            yield return Command("material_import", new JObject { ["material_id"] = Text(100), ["allow_online"] = new JObject { ["type"] = "boolean" } }, new JArray("material_id"), false);
            yield return Command("material_apply", new JObject { ["material_id"] = Text(100), ["tiling_x"] = Number("number", .05, 100), ["tiling_y"] = Number("number", .05, 100), ["include_children"] = new JObject { ["type"] = "boolean" }, ["terrain_layer_index"] = Number("integer", 0, 7) }, new JArray("material_id"), true);
        }
        static JObject Command(string name, JObject args, JArray required, bool target) => new JObject {
            ["type"] = "object", ["additionalProperties"] = false, ["properties"] = new JObject {
                ["tool"] = new JObject { ["type"] = "string", ["const"] = name }, ["name"] = Text(160, 0), ["target"] = target ? Text(200) : new JObject { ["type"] = "string", ["const"] = "" },
                ["args"] = new JObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = args, ["required"] = required }
            }, ["required"] = new JArray("tool", "name", "target", "args") };

        internal static void Validate(EditorCommand command, Func<string, GameObject> resolve)
        {
            var schema = CommandSchemas().Single(c => (string)c["properties"]["tool"]["const"] == command.tool);
            var properties = (JObject)schema["properties"]["args"]["properties"];
            if (command.args.Properties().Any(p => properties[p.Name] == null)) throw new ArgumentException("Unknown material argument.");
            foreach (string key in (JArray)schema["properties"]["args"]["required"]) if (command.args[key] == null) throw new ArgumentException("Missing " + key);
            foreach (var property in command.args.Properties())
            {
                var definition = properties[property.Name]; var value = property.Value; string type = (string)definition["type"];
                if (type == "string")
                {
                    if (value.Type != JTokenType.String || ((string)value).Length < ((int?)definition["minLength"] ?? 0) || ((string)value).Length > ((int?)definition["maxLength"] ?? 200) || ((string)value).Any(char.IsControl)) throw new ArgumentException("Invalid " + property.Name);
                    if (definition["enum"] is JArray choices && !choices.Values<string>().Contains((string)value)) throw new ArgumentException("Invalid " + property.Name);
                }
                else if (type == "boolean") { if (value.Type != JTokenType.Boolean) throw new ArgumentException(property.Name + " must be boolean."); }
                else
                {
                    if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float || type == "integer" && value.Type != JTokenType.Integer) throw new ArgumentException(property.Name + " must be " + type);
                    double number = (double)value;
                    if (double.IsNaN(number) || double.IsInfinity(number) || number < (double)definition["minimum"] || number > (double)definition["maximum"]) throw new ArgumentException(property.Name + " is out of range.");
                }
            }
            if (IsAsync(command.tool))
            {
                if (!string.IsNullOrEmpty(command.target)) throw new ArgumentException(command.tool + " requires an empty target.");
                if (command.tool == "material_search" && string.IsNullOrWhiteSpace((string)command.args["query"])) throw new ArgumentException("Material search query is empty.");
            }
            if (command.tool != "material_search") WorldMaterialLibrary.ValidateMaterialId((string)command.args["material_id"]);
            if (command.tool == "material_apply")
            {
                var target = resolve(command.target); bool children = (bool?)command.args["include_children"] ?? false;
                var terrain = target.GetComponent<Terrain>();
                if (!terrain && !(children ? target.GetComponentsInChildren<Renderer>(true).Length > 0 : target.GetComponent<Renderer>() != null)) throw new ArgumentException("Material target needs a Renderer/Terrain; include_children must be explicit for a root.");
                int layer = (int?)command.args["terrain_layer_index"] ?? 0;
                if (terrain && (!terrain.terrainData || layer >= Math.Max(1, terrain.terrainData.terrainLayers.Length))) throw new ArgumentException("Terrain layer index must refer to an existing layer, or zero on an empty Terrain.");
                string id = (string)command.args["material_id"];
                if (id.StartsWith("polyhaven:", StringComparison.Ordinal) && !WorldMaterialLibrary.IsImported(id)) throw new ArgumentException("Import this online material in a standalone material_import step first.");
            }
        }

        internal static EditorCommandResult Apply(EditorCommand command, Func<string, GameObject> resolve, SceneBuildTransaction transaction)
        {
            var args = command.args;
            var applied = WorldMaterialLibrary.Apply(resolve(command.target), (string)args["material_id"], (float?)args["tiling_x"] ?? 1, (float?)args["tiling_y"] ?? 1,
                (bool?)args["include_children"] ?? false, (int?)args["terrain_layer_index"] ?? 0, false);
            RegisterCreated(transaction,applied.CreatedAssets);
            return new EditorCommandResult { Success = true, Tool = command.tool, Target = command.target, Summary = JsonConvert.SerializeObject(applied), CreatedAssets = applied.CreatedAssets };
        }

        internal static async Task<EditorCommandResult> ExecuteAsync(EditorCommand command, CancellationToken token, Action beforeMutation)
        {
            if (command.tool == "material_search")
            {
                var found = await WorldMaterialLibrary.SearchAsync((string)command.args["query"], (string)command.args["source"] ?? "any", (int?)command.args["limit"] ?? 8, token);
                token.ThrowIfCancellationRequested();
                return new EditorCommandResult { Success = true, Tool = command.tool, Summary = JsonConvert.SerializeObject(found) };
            }
            MaterialImportResult imported = null;
            try
            {
                imported = await WorldMaterialLibrary.ImportAsync((string)command.args["material_id"], (bool?)command.args["allow_online"] ?? false, token, beforeMutation);
                token.ThrowIfCancellationRequested();
                return new EditorCommandResult { Success = true, Tool = command.tool, Summary = JsonConvert.SerializeObject(imported), CreatedAssets = imported.CreatedAssets };
            }
            catch
            {
                if (imported != null && !imported.CacheHit)
                {var cleanup=new SceneBuildTransaction();RegisterCreated(cleanup,imported.CreatedAssets);cleanup.RollbackAssets();}
                throw;
            }
        }
        static void RegisterCreated(SceneBuildTransaction transaction,IEnumerable<string> paths)
        {
            foreach(string path in paths)
            {
                if(AssetDatabase.IsValidFolder(path)){if(!transaction.Folders.Contains(path))transaction.Folders.Add(path);}
                else if(!transaction.Assets.Contains(path))transaction.Assets.Add(path);
            }
        }
    }
}
