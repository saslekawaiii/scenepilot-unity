using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;
namespace WorldWeaver.Editor.Assistant
{
    internal sealed class WorldGameplayToolResult { internal GameObject Target; internal string Summary; }
    /// <summary>Typed vertical-slice tools. Called inside the executor's existing transaction/Undo scope.</summary>
    internal static class WorldGameplayTools
    {
        internal const string ToolInstructions = @"GAMEPLAY TOOLS:
create_village: name; target empty; args {houses?:1..12,width?:96..512,seed?:integer,trees?:0..60,player?:bool,quest?:bool}. Creates a connected village, central square, entrances, roads, north_gate, guard, real NavMesh and optional playable player/letter delivery quest. Local procedural art, not imported models. This is a specialized complete world; don't also create_world.
bake_navigation: target owned world; args {}. Builds actual Unity NavMesh, validates named connections.
set_navigation: target existing owned NPC; args {places:array of 2..16 semantic place keys in its world,speed?:.1..10}. Validates complete NavMesh paths and sets named patrol, never straight-line movement.
create_player: target owned world; args {position?:[x,y,z]}. Playable WASD character, orbit camera, E quest interaction, inventory and Save/Load UI; at most one per world.
create_delivery_quest: target owned world; args {giver:existing NPC target,recipient:existing NPC target,reward?:1..100}. Real letter inventory, one-time reward, scoped NPC event memory and saved quest progress. Does not generate dialogue with an LLM.
catalog_assets: target empty; args {query?:text,limit?:1..40}. Read-only prefab catalog with bounds/renderers/colliders/scripts and render-pipeline metadata.
instantiate_prefab: name; target optional owned parent; args {path:Assets/...prefab,position?:[x,y,z],scale?:[x,y,z],allow_scripts?:bool}. Copies a real existing project prefab; scripts denied unless explicitly allowed by user review. Never downloads assets.
configure_world_time: target owned world; args {hour:0..<24,day?:1..1000000,running?:bool,day_seconds?:30..3600}. Updates actual clock, gates and schedules.
configure_npc_routine: target owned NPC in a clocked world; args {day_place:semantic key,night_place:semantic key,fallback_place?:key,start_hour?:0..23.99,end_hour?:0..23.99,enabled?:bool}. Actual NavMesh schedule, unreachable primary goal remains blocked even at fallback.
configure_npc_ai: target existing NPC; args {enabled?:bool,social?:bool,autonomous?:bool,topic?:text,cooldown_seconds?:15..300,assign_selected_model?:bool}. Enables/disables freeform AI per character and optional nearby NPC-to-NPC conversations, bounded to three turns with player priority. assign_selected_model explicitly copies the chat's chosen model route; other settings preserve existing routes. Existing NavMesh supports walking up to nearby characters; never invent a path. Generated speech never changes quest/reward state by itself.
configure_gate: target owned gate; args {open_hour:0..23.99,close_hour:0..23.99}. Physical panel collider/carved navigation gate; closed connection is intentionally blocked.
validate_world: target owned world; args {}. Read-only semantic graph/NavMesh/quest/player/asset validation; reports failures, not an unverified success.";
        private static readonly string[] Names={"configure_world_time","configure_npc_routine","configure_npc_ai","configure_gate","create_village","bake_navigation","set_navigation","create_player","create_delivery_quest","catalog_assets","instantiate_prefab","validate_world"};
        internal static bool Handles(string tool)=>Names.Contains(tool);
        internal static bool IsReadOnly(string tool)=>tool=="catalog_assets"||tool=="validate_world";
        private static JObject S(string type)=>new JObject { ["type"]=type };
        private static JObject N(string type,double minimum,double maximum)=>new JObject { ["type"]=type,["minimum"]=minimum,["maximum"]=maximum };
        private static JObject V()=>new JObject { ["type"]="array",["items"]=N("number",-100000,100000),["minItems"]=3,["maxItems"]=3 };
        internal static IEnumerable<JObject> CommandSchemas()
        {
            var definitions=new Dictionary<string,JObject> {
                ["configure_world_time"]=new JObject { ["hour"]=N("number",0,23.999),["day"]=N("integer",1,1000000),["running"]=S("boolean"),["day_seconds"]=N("number",30,3600) },
                ["configure_npc_routine"]=new JObject { ["day_place"]=S("string"),["night_place"]=S("string"),["fallback_place"]=S("string"),["start_hour"]=N("number",0,23.999),["end_hour"]=N("number",0,23.999),["enabled"]=S("boolean") },
                ["configure_npc_ai"]=new JObject { ["enabled"]=S("boolean"),["social"]=S("boolean"),["autonomous"]=S("boolean"),["topic"]=new JObject { ["type"]="string",["maxLength"]=800 },["cooldown_seconds"]=N("number",15,300),["assign_selected_model"]=S("boolean") },
                ["configure_gate"]=new JObject { ["open_hour"]=N("number",0,23.999),["close_hour"]=N("number",0,23.999) },
                ["create_village"]=new JObject { ["houses"]=N("integer",1,12),["width"]=N("number",96,512),["seed"]=N("integer",int.MinValue,int.MaxValue),["trees"]=N("integer",0,60),["player"]=S("boolean"),["quest"]=S("boolean") },
                ["bake_navigation"]=new JObject(),["validate_world"]=new JObject(),
                ["set_navigation"]=new JObject { ["places"]=new JObject { ["type"]="array",["items"]=new JObject { ["type"]="string",["minLength"]=1,["maxLength"]=160 },["minItems"]=2,["maxItems"]=16 },["speed"]=N("number",.1,10) },
                ["create_player"]=new JObject { ["position"]=V() },
                ["create_delivery_quest"]=new JObject { ["giver"]=S("string"),["recipient"]=S("string"),["reward"]=N("integer",1,100) },
                ["catalog_assets"]=new JObject { ["query"]=new JObject { ["type"]="string",["maxLength"]=120 },["limit"]=N("integer",1,40) },
                ["instantiate_prefab"]=new JObject { ["path"]=new JObject { ["type"]="string",["maxLength"]=240 },["position"]=V(),["scale"]=V(),["allow_scripts"]=S("boolean") }
            };
            foreach(var pair in definitions)
            {
                var required=pair.Key=="configure_world_time"?new JArray("hour"):pair.Key=="configure_gate"?new JArray("open_hour","close_hour"):pair.Key=="configure_npc_routine"?new JArray("day_place","night_place"):pair.Key=="set_navigation"?new JArray("places"):pair.Key=="create_delivery_quest"?new JArray("giver","recipient"):pair.Key=="instantiate_prefab"?new JArray("path"):new JArray();
                yield return new JObject { ["type"]="object",["additionalProperties"]=false,["properties"]=new JObject { ["tool"]=new JObject { ["type"]="string",["const"]=pair.Key },["name"]=S("string"),["target"]=S("string"),["args"]=new JObject { ["type"]="object",["additionalProperties"]=false,["properties"]=pair.Value,["required"]=required } },["required"]=new JArray("tool","name","target","args") };
            }
        }
        internal static int EstimateCost(EditorCommand c)=>c.tool=="create_village"?45+9*((int?)c.args["houses"]??5)+4*((int?)c.args["trees"]??12)+2*Mathf.CeilToInt((((float?)c.args["width"]??128)/2-2)/4):c.tool=="create_player"?3:c.tool=="create_delivery_quest"?1:c.tool=="instantiate_prefab"?Prefab(c.args).GetComponentsInChildren<Transform>(true).Length:0;
        internal static void Validate(EditorCommand c,Func<string,GameObject> resolve)
        {
            var definition=CommandSchemas().First(s=>(string)s["properties"]["tool"]["const"]==c.tool);
            var allowed=(JObject)definition["properties"]["args"]["properties"];
            if(c.args.Properties().Any(p=>allowed.Property(p.Name)==null))throw new ArgumentException("Unknown gameplay tool argument.");
            foreach(string key in (JArray)definition["properties"]["args"]["required"])if(c.args[key]==null)throw new ArgumentException("Missing "+key);
            foreach(var pair in c.args.Properties())ValidateValue(pair.Value,(JObject)allowed[pair.Name],pair.Name);
            if(c.tool=="create_village"||c.tool=="catalog_assets") { if(!string.IsNullOrEmpty(c.target))throw new ArgumentException(c.tool+" requires empty target."); }
            else if(c.tool!="instantiate_prefab"||!string.IsNullOrEmpty(c.target)) { var go=resolve(c.target); if(!go||c.tool!="configure_npc_ai"&&!go.GetComponent<GeneratedSceneObject>())throw new ArgumentException("Gameplay target must be owned."); }
            if(c.tool=="configure_npc_ai")
            {
                var npc=resolve(c.target).GetComponent<IntelligentNPC>();
                if(!npc||c.args.Count==0)throw new ArgumentException("AI configuration needs an existing NPC and at least one setting.");
                if(string.IsNullOrWhiteSpace(npc.CharacterId)||npc.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<IntelligentNPC>(true)).Count(n=>n.CharacterId==npc.CharacterId)>1)throw new ArgumentException("NPC CharacterId must be unique before enabling conversation memory.");
                bool needsRoute=(bool?)c.args["enabled"]==true||(bool?)c.args["social"]==true||(bool?)c.args["autonomous"]==true;
                if(needsRoute&&!npc.Models&&(bool?)c.args["assign_selected_model"]!=true)throw new ArgumentException("Assign a model route or set assign_selected_model=true.");
                if(needsRoute&&npc.Models&&(bool?)c.args["assign_selected_model"]!=true) { npc.Models.Resolve(ModelTask.Dialogue).Validate();npc.Models.Resolve(ModelTask.Planning).Validate(); }
            }
            if(c.tool=="instantiate_prefab")
            {
                var prefab=Prefab(c.args); if(prefab.GetComponentsInChildren<MonoBehaviour>(true).Any()&&(bool?)c.args["allow_scripts"]!=true)throw new ArgumentException("Prefab contains scripts. Review before explicitly allowing scripts.");
                if(c.args["scale"] is JArray scale && scale.Any(n=>(double)n<.01||(double)n>100))throw new ArgumentException("Scale must be .01..100.");
            }
            if(c.tool=="configure_world_time"&&!resolve(c.target).GetComponent<WorldClock>())throw new ArgumentException("Target world has no WorldClock.");
            if(c.tool=="configure_gate"&&!resolve(c.target).GetComponent<WorldGate>())throw new ArgumentException("Target is not a configured physical WorldGate.");
            if(c.tool=="configure_npc_routine")
            {
                var npc=resolve(c.target);var nav=npc.GetComponentInParent<WorldNavigation>();if(!npc.GetComponent<IntelligentNPC>()||!nav||!nav.GetComponent<WorldClock>())throw new ArgumentException("Routine needs an NPC in a clocked navigation world.");
                nav.Find((string)c.args["day_place"]);nav.Find((string)c.args["night_place"]);if(c.args["fallback_place"]!=null)nav.Find((string)c.args["fallback_place"]);
            }
            if(c.tool=="set_navigation"&&!resolve(c.target).GetComponent<IntelligentNPC>())throw new ArgumentException("Patrol target must be NPC.");
            if(c.tool=="create_delivery_quest")foreach(string key in new[]{"giver","recipient"})if(!resolve((string)c.args[key]).GetComponent<IntelligentNPC>())throw new ArgumentException("Quest participants must be existing NPCs.");
        }
        private static void ValidateValue(JToken value,JObject schema,string key)
        {
            string type=(string)schema["type"];
            if(type=="number"||type=="integer") { if(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)throw new ArgumentException(key+" must be numeric."); double n=(double)value; if(double.IsNaN(n)||double.IsInfinity(n)||n<(double)schema["minimum"]||n>(double)schema["maximum"]||(type=="integer"&&n!=Math.Truncate(n)))throw new ArgumentException(key+" out of range."); }
            else if(type=="boolean"&&value.Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be boolean.");
            else if(type=="string"&&(value.Type!=JTokenType.String||((string)value).Length>((int?)schema["maxLength"]??240)||((string)value).Length<((int?)schema["minLength"]??0)))throw new ArgumentException("Invalid "+key);
            else if(type=="array") { if(!(value is JArray a)||a.Count<(int)schema["minItems"]||a.Count>(int)schema["maxItems"])throw new ArgumentException("Invalid "+key+" array.");foreach(var item in a)ValidateValue(item,(JObject)schema["items"],key); }
        }
        private static GameObject Prefab(JObject a)
        {
            string path=(string)a["path"];
            if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\')||!path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Expected an existing Assets/...prefab path.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path)??throw new ArgumentException("Prefab not found: "+path);
        }
        internal static WorldGameplayToolResult Execute(EditorCommand c,SceneBuildTransaction tx,Transform parent,Func<string,GameObject> resolve,OrchestratorProfile routes)
        {
            Validate(c,resolve); GameObject target=string.IsNullOrEmpty(c.target)?null:resolve(c.target); var a=c.args; string summary;
            switch(c.tool)
            {
                case "configure_world_time":
                    var clock=target.GetComponent<WorldClock>();Undo.RecordObject(clock,"World time");foreach(var controlledGate in target.GetComponentsInChildren<WorldGate>(true))if(controlledGate.Panel)Undo.RecordObject(controlledGate.Panel,"Clock gate panel");if(a["running"]!=null)clock.Running=(bool)a["running"];if(a["day_seconds"]!=null)clock.DaySeconds=(float)a["day_seconds"];clock.SetTime((float)a["hour"],(int?)a["day"]??-1);foreach(var gate in target.GetComponentsInChildren<WorldGate>(true))gate.ApplyClock();summary=clock.Snapshot().ToString(Formatting.None);break;
                case "configure_gate":var gateComponent=target.GetComponent<WorldGate>();Undo.RecordObject(gateComponent,"Gate hours");if(gateComponent.Panel)Undo.RecordObject(gateComponent.Panel,"Gate panel state");gateComponent.OpenHour=(float)a["open_hour"];gateComponent.CloseHour=(float)a["close_hour"];gateComponent.ApplyClock();summary=gateComponent.Snapshot().ToString(Formatting.None);break;
                case "configure_npc_routine":Routine(target,a);summary=target.GetComponent<NpcDailyRoutine>().Snapshot().ToString(Formatting.None);break;
                case "configure_npc_ai":
                    ConfigureNpcAi(target,a,routes);
                    var configured=target.GetComponent<IntelligentNPC>();var social=target.GetComponent<NpcSocialAutonomy>();
                    summary=new JObject { ["aiEnabled"]=configured.AiEnabled,["autonomous"]=configured.Autonomous,["social"]=social&&social.enabled&&social.Enabled,["model"]=configured.Models&&configured.Models.Dialogue?configured.Models.Dialogue.Model:null,["conversationMemoryLimit"]=32,["ambientRequestsPerMinute"]=4,["playerHasPriority"]=true }.ToString(Formatting.None);break;
                case "create_village":target=Village(tx,parent,c.name,a,routes);summary=Report(target).ToString(Formatting.None);break;
                case "bake_navigation":Bake(tx,target);summary=Report(target).ToString(Formatting.None);break;
                case "set_navigation":SetNavigation(target,a);summary="Настроен маршрут по именованным точкам на действительной NavMesh.";break;
                case "create_player":target=Player(tx,target,a["position"]!=null?Vector(a["position"]):new Vector3(0,4,5));summary="Создан игрок: WASD, камера, E и сохранения.";break;
                case "create_delivery_quest":target=Quest(tx,target,resolve((string)a["giver"]),resolve((string)a["recipient"]),(int?)a["reward"]??5);summary="Квест письма с реальным инвентарём и однократной наградой создан.";break;
                case "catalog_assets":summary=Catalog((string)a["query"]??"",(int?)a["limit"]??20).ToString(Formatting.None);break;
                case "instantiate_prefab":target=Instantiate(tx,parent,c.name,a);summary="Размещён существующий prefab: "+(string)a["path"];break;
                case "validate_world":summary=Report(target).ToString(Formatting.None);break;
                default:throw new ArgumentException("Unknown gameplay tool.");
            }
            return new WorldGameplayToolResult { Target=target,Summary=summary };
        }
        private static Vector3 Vector(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
        private static void ConfigureNpcAi(GameObject target,JObject args,OrchestratorProfile routes)
        {
            var npc=target.GetComponent<IntelligentNPC>();Undo.RecordObject(npc,"Configure NPC intelligence");
            if((bool?)args["assign_selected_model"]==true)
            { if(!routes)throw new ArgumentException("The selected chat model route is unavailable.");routes.Resolve(ModelTask.Dialogue).Validate();routes.Resolve(ModelTask.Planning).Validate();npc.ReleaseServices();npc.Models=routes; }
            if(args["enabled"]!=null)npc.AiEnabled=(bool)args["enabled"];
            if(args["autonomous"]!=null)npc.Autonomous=(bool)args["autonomous"];
            if(args["cooldown_seconds"]!=null)npc.PlanningCooldown=(float)args["cooldown_seconds"];
            var social=target.GetComponent<NpcSocialAutonomy>();
            if((bool?)args["social"]==true&&!social)social=Undo.AddComponent<NpcSocialAutonomy>(target);
            if(social)
            {
                Undo.RecordObject(social,"Configure NPC conversations");
                if(args["social"]!=null)social.Enabled=social.enabled=(bool)args["social"];
                if(args["topic"]!=null)social.Topic=(string)args["topic"];
                if(args["cooldown_seconds"]!=null)social.CooldownSeconds=(float)args["cooldown_seconds"];
                social.MaxTurns=3;EditorUtility.SetDirty(social);
            }
            EditorUtility.SetDirty(npc);
        }
        private static WorldPlace Place(GameObject go,string key,WorldPlaceKind kind)
        {
            var place=go.GetComponent<WorldPlace>()??Undo.AddComponent<WorldPlace>(go); place.Key=key;place.Kind=kind;
            var entity=go.GetComponent<WorldEntity>()??Undo.AddComponent<WorldEntity>(go);entity.Id=place.PersistentId;entity.Interactable=kind==WorldPlaceKind.Npc;
            return place;
        }
        private static GameObject Village(SceneBuildTransaction tx,Transform parent,string name,JObject a,OrchestratorProfile routes)
        {
            int houses=(int?)a["houses"]??5,trees=(int?)a["trees"]??12;float width=(float?)a["width"]??128;
            var world=WorldComposer.World(tx,parent,name,new JObject { ["style"]="forest",["width"]=width,["length"]=width,["height"]=16,["mountainStrength"]=.15,["waterLevel"]=0,["trees"]=0,["buildings"]=0,["rocks"]=0,["seed"]=(int?)a["seed"]??42 });
            var terrain=world.GetComponentInChildren<Terrain>();var data=terrain.terrainData;int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);
            // A level village terrace with a smooth apron keeps streets and entrances mutually reachable.
            float radius=Mathf.Max(36,12+houses*1.8f);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {float px=x/(float)(n-1)*width-width/2,pz=z/(float)(n-1)*width-width/2;float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((new Vector2(px,pz).magnitude-radius)/8));heights[z,x]=Mathf.Lerp(.25f,heights[z,x],blend);}
            data.SetHeights(0,0,heights);WorldComposer.Alpha(data,terrain.GetComponent<GeneratedTerrain>().Specification);EditorUtility.SetDirty(data);
            var save=Undo.AddComponent<WorldSaveState>(world);var clock=Undo.AddComponent<WorldClock>(world);Undo.AddComponent<WorldEventJournal>(world);
            var square=tx.Primitive("Площадь",PrimitiveType.Cube,world.transform,tx.Material("Square",new Color(.51f,.46f,.35f)),new Vector3(0,4.02f,0),new Vector3(9,.04f,9),false);
            square.GetComponent<GeneratedSceneObject>().Kind="square";var center=Place(square,"square",WorldPlaceKind.Square);Anchor(square,terrain,.02f);
            var gate=tx.Empty("Северные ворота",world.transform,"gate");gate.transform.position=new Vector3(0,4,30);var gatePlace=Place(gate,"north_gate",WorldPlaceKind.Gate);gatePlace.ArrivalOffset=new Vector3(0,0,-2.5f);Anchor(gate,terrain,0);
            var wood=tx.Material("Gate wood",new Color(.29f,.20f,.12f));
            tx.Primitive("Left post",PrimitiveType.Cube,gate.transform,wood,new Vector3(-2,1.7f,0),new Vector3(.6f,3.4f,.6f));
            tx.Primitive("Right post",PrimitiveType.Cube,gate.transform,wood,new Vector3(2,1.7f,0),new Vector3(.6f,3.4f,.6f));
            tx.Primitive("Lintel",PrimitiveType.Cube,gate.transform,wood,new Vector3(0,3.3f,0),new Vector3(4.6f,.5f,.6f));
            var outsideGo=tx.Empty("За северными воротами",world.transform,"place");outsideGo.transform.position=new Vector3(0,4,35);Anchor(outsideGo,terrain,0);var outside=Place(outsideGo,"outside_gate",WorldPlaceKind.Waypoint);outside.Connections=new[]{gatePlace};
            var shelterGo=tx.Empty("Северное укрытие",world.transform,"place");shelterGo.transform.position=new Vector3(5,4,36);Anchor(shelterGo,terrain,0);Place(shelterGo,"outside_shelter",WorldPlaceKind.Waypoint);
            var panel=tx.Primitive("Створка ворот",PrimitiveType.Cube,gate.transform,wood,new Vector3(0,1.5f,0),new Vector3(4,3,.65f));var obstacle=Undo.AddComponent<NavMeshObstacle>(panel);obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=true;obstacle.carvingTimeToStationary=0;
            var gateState=Undo.AddComponent<WorldGate>(gate);gateState.Clock=clock;gateState.Panel=panel;gateState.Inside=gatePlace;gateState.Outside=outside;gateState.ApplyClock();
            int segments=Mathf.CeilToInt((width/2-2)/4);float span=(width/2-2)/segments;
            for(int side=-1;side<=1;side+=2)for(int segment=0;segment<segments;segment++)
            {
                float x=side*(2+span*(segment+.5f));float y=terrain.SampleHeight(new Vector3(x,0,30))+terrain.transform.position.y;
                var fence=tx.Primitive("Северная ограда "+side+" "+segment,PrimitiveType.Cube,world.transform,wood,new Vector3(x,y+1.5f,30),new Vector3(span+.05f,5,.65f));fence.GetComponent<GeneratedSceneObject>().Kind="fence";Anchor(fence,terrain,1.5f);
            }
            Road(tx,world,terrain,center,gatePlace,"Дорога к воротам");Road(tx,world,terrain,gatePlace,outside,"Дорога за воротами");var connections=new List<WorldPlace>{gatePlace};gatePlace.Connections=new[]{center,outside};
            for(int i=0;i<houses;i++)
            {
                float angle=(i+.4f)*Mathf.PI*2/houses;var p=new Vector3(Mathf.Cos(angle)*20,4,Mathf.Sin(angle)*20);
                var house=WorldComposer.ObjectOfKind(tx,world.transform,"Дом "+(i+1),"house");house.transform.position=p;house.transform.rotation=Quaternion.LookRotation(p-new Vector3(0,4,0));Anchor(house,terrain,0);
                var housePlace=Place(house,"house_"+(i+1),WorldPlaceKind.House);
                var entry=tx.Empty("Вход дома "+(i+1),world.transform,"entrance");entry.transform.position=p-(p-new Vector3(0,4,0)).normalized*3.6f;Anchor(entry,terrain,0);
                var entrance=Place(entry,"entrance_"+(i+1),WorldPlaceKind.Entrance);housePlace.Connections=new[]{entrance};housePlace.ArrivalOffset=house.transform.InverseTransformVector(entry.transform.position-house.transform.position);
                entrance.Connections=new[]{center};connections.Add(entrance);Road(tx,world,terrain,center,entrance,"Дорога к дому "+(i+1));
            }
            center.Connections=connections.ToArray();
            // Decoration stays outside the settlement to preserve the authored walking corridors.
            if(trees>0)WorldComposer.Populate(tx,world.transform,terrain,"tree",trees,new System.Random((int?)a["seed"]??42),new List<Vector3>{new Vector3(0,4,0)},"forest",new Vector3(-width*.39f,0,0),new Vector2(width*.08f,width*.72f),3.5f);
            var giver=Npc(tx,world,"Житель","giver",new Vector3(-3,4,0),"Передать письмо стражу.",routes);
            var guard=Npc(tx,world,"Игнат","guard",new Vector3(1,4,27),"Охранять северные ворота и патрулировать площадь.",routes);
            Bake(tx,world);SetNavigation(guard,new JObject { ["places"]=new JArray("north_gate","square"),["speed"]=1.5 });
            Routine(guard,new JObject { ["day_place"]="north_gate",["night_place"]="entrance_1",["fallback_place"]="outside_shelter" });
            if((bool?)a["player"]!=false)Player(tx,world,new Vector3(0,4,6));
            if((bool?)a["quest"]!=false)Quest(tx,world,giver,guard,5);
            foreach(var npc in world.GetComponentsInChildren<IntelligentNPC>()) { npc.ContextSources=npc.ContextSources.Concat(new MonoBehaviour[]{gateState}).Distinct().Take(8).ToArray(); }
            foreach(var npc in world.GetComponentsInChildren<IntelligentNPC>())npc.KnownEntities=world.GetComponentsInChildren<WorldEntity>().Where(e=>e.gameObject!=npc.gameObject).OrderByDescending(e=>e.GetComponent<WorldPlayer>()||e.GetComponent<IntelligentNPC>()).ThenByDescending(e=>e.GetComponent<WorldPlace>()&&e.GetComponent<WorldPlace>().Kind==WorldPlaceKind.Gate).ToList();
            var report=Report(world);if((int)report["navigationFailures"]!=0)throw new InvalidOperationException("Village navigation validation failed: "+report.ToString(Formatting.None));
            return world;
        }
        private static GameObject Npc(SceneBuildTransaction tx,GameObject world,string name,string key,Vector3 position,string goal,OrchestratorProfile routes)
        {
            var go=WorldComposer.Npc(tx,world.transform,name,key=="guard"?"страж":"житель");go.transform.position=position;var place=Place(go,key,WorldPlaceKind.Npc);Anchor(go,world.GetComponentInChildren<Terrain>(),0);
            Undo.AddComponent<Inventory>(go);var npc=Undo.AddComponent<IntelligentNPC>(go);npc.CharacterId=place.PersistentId;npc.NpcName=name;npc.Role=key=="guard"?"страж":"житель";npc.Personality="Спокойный, доброжелательный, говорит кратко.";npc.Goals=goal;npc.Autonomous=false;
            if(!routes)
            {
                var model=tx.Asset(ScriptableObject.CreateInstance<ModelProfile>(),"NpcLocalModel");model.Model="qwen3.5:9b";model.MaxTokens=512;model.TimeoutSeconds=150;model.Retries=0;model.Temperature=.25f;model.ContextWindow=8192;
                routes=tx.Asset(ScriptableObject.CreateInstance<OrchestratorProfile>(),"NpcRoutes");routes.Dialogue=routes.Planning=routes.Terrain=model;
            }
            Undo.AddComponent<NpcRelationships>(go);
            npc.Models=routes;var knowledge=ScriptableObject.CreateInstance<KnowledgeBase>();knowledge.Documents.Add(new KnowledgeDocument { Id=key+"-lore",Scope=npc.CharacterId,Title="Деревня",Kind=MemoryKind.StaticKnowledge,Text="Северные ворота открываются в шесть утра. Площадь находится в центре деревни. Житель на площади передаёт письмо стражу Игнату у северных ворот. За доставку письма полагается пять монет. Днём Игнат охраняет ворота, ночью отдыхает у входа первого дома." });npc.Knowledge=tx.Asset(knowledge,key+"Knowledge");
            npc.ContextSources=world.GetComponents<MonoBehaviour>().Where(c=>c is IWorldContextSource).Concat(new[]{go.GetComponent<NpcRelationships>()}).ToArray();
            var agent=Undo.AddComponent<NavMeshAgent>(go);agent.radius=.35f;agent.height=1.8f;agent.baseOffset=0;agent.speed=1.5f;
            return go;
        }
        private static void Road(SceneBuildTransaction tx,GameObject world,Terrain terrain,WorldPlace from,WorldPlace to,string name)
        {
            Vector3 delta=to.ArrivalPosition-from.ArrivalPosition;var road=tx.Primitive(name,PrimitiveType.Cube,world.transform,tx.Material("Road",new Color(.48f,.35f,.22f)),(from.ArrivalPosition+to.ArrivalPosition)*.5f+Vector3.up*.035f,new Vector3(2.5f,.03f,delta.magnitude),false);
            road.transform.rotation=Quaternion.LookRotation(delta);road.GetComponent<GeneratedSceneObject>().Kind="road";Place(road,"road_"+to.Key,WorldPlaceKind.Road);Anchor(road,terrain,.035f);
        }
        private static void Anchor(GameObject go,Terrain terrain,float offset)
        {
            var anchor=go.GetComponent<WorldSurfaceAnchor>()??Undo.AddComponent<WorldSurfaceAnchor>(go);anchor.Offset=offset;anchor.Terrain=terrain;anchor.Reanchor();
        }
        internal static void RevalidateAfterTerrainUpdate(SceneBuildTransaction tx,GameObject world)
        {
            var terrain=world.GetComponentInChildren<Terrain>();if(!terrain)terrain=world.GetComponent<Terrain>();if(!terrain)return;
            foreach(var anchor in Object.FindObjectsByType<WorldSurfaceAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(a=>a.gameObject.scene==terrain.gameObject.scene&&a.Terrain==terrain)) { Undo.RecordObject(anchor.transform,"Reanchor world object");anchor.Reanchor(); }
            var navigation=terrain.GetComponentInParent<WorldNavigation>();if(navigation)Bake(tx,navigation.gameObject);
        }
        internal static void MarkLightAnchor(Light light,Terrain terrain)
        {
            if(!light||!terrain||light.type==LightType.Directional)return;
            var anchor=light.GetComponent<WorldSurfaceAnchor>()??Undo.AddComponent<WorldSurfaceAnchor>(light.gameObject);Undo.RecordObject(anchor,"Anchor light");anchor.Terrain=terrain;anchor.AboveWater=true;anchor.Offset=Mathf.Max(3,light.transform.position.y-anchor.SurfaceHeight());
        }
        private static void Bake(SceneBuildTransaction tx,GameObject target)
        {
            var nav=target.GetComponent<WorldNavigation>()??Undo.AddComponent<WorldNavigation>(target);Undo.RecordObject(nav,"Bake world navigation");var data=nav.Build();nav.BakedData=tx.Asset(data,"WorldNavMesh");nav.Register(nav.BakedData);
        }
        private static void SetNavigation(GameObject target,JObject args)
        {
            var nav=target.GetComponentInParent<WorldNavigation>();if(!nav)throw new ArgumentException("Bake navigation in the NPC's world first.");
            var places=((JArray)args["places"]).Select(p=>nav.Find((string)p)).ToArray();
            for(int i=0;i<places.Length;i++)if(!WorldNavigation.TryPath(i==0?target.transform.position:places[i-1].ArrivalPosition,places[i].ArrivalPosition,out _))throw new ArgumentException("Named patrol contains unreachable destination: "+places[i].Key);
            if(!WorldNavigation.TryPath(places.Last().ArrivalPosition,places[0].ArrivalPosition,out _))throw new ArgumentException("Patrol cannot return to its first point.");
            var agent=target.GetComponent<NavMeshAgent>()??Undo.AddComponent<NavMeshAgent>(target);Undo.RecordObject(agent,"NPC navigation agent");agent.radius=.35f;agent.height=1.8f;agent.speed=(float?)args["speed"]??2;
            var behavior=target.GetComponent<SceneBehavior>()??Undo.AddComponent<SceneBehavior>(target);Undo.RecordObject(behavior,"Named NPC patrol");behavior.Mode=SceneBehaviorKind.Patrol;behavior.NamedWaypoints=places;behavior.Waypoints=Array.Empty<Vector3>();behavior.Speed=agent.speed;
        }
        private static void Routine(GameObject target,JObject args)
        {
            var nav=target.GetComponentInParent<WorldNavigation>();var routine=target.GetComponent<NpcDailyRoutine>()??Undo.AddComponent<NpcDailyRoutine>(target);Undo.RecordObject(routine,"NPC daily schedule");routine.Clock=nav.GetComponent<WorldClock>();routine.DayPlace=nav.Find((string)args["day_place"]);routine.NightPlace=nav.Find((string)args["night_place"]);routine.BlockedFallback=args["fallback_place"]!=null?nav.Find((string)args["fallback_place"]):null;routine.StartHour=(float?)args["start_hour"]??6;routine.EndHour=(float?)args["end_hour"]??20;routine.enabled=(bool?)args["enabled"]??true;
            var behavior=target.GetComponent<SceneBehavior>();if(behavior){Undo.RecordObject(behavior,"Schedule owns NPC navigation");behavior.enabled=!routine.enabled;}
            var npc=target.GetComponent<IntelligentNPC>();Undo.RecordObject(npc,"NPC factual dynamic context");npc.ContextSources=nav.GetComponents<MonoBehaviour>().Where(c=>c is IWorldContextSource).Concat(nav.GetComponentsInChildren<WorldGate>()).Concat(new MonoBehaviour[]{routine,target.GetComponent<NpcRelationships>()}).Where(c=>c).Distinct().Take(8).ToArray();
            routine.ResetAfterLoad();
        }
        private static GameObject Player(SceneBuildTransaction tx,GameObject world,Vector3 position)
        {
            if(world.GetComponentInChildren<WorldPlayer>(true))throw new ArgumentException("This world already has a player.");
            if(!world.GetComponent<WorldSaveState>())Undo.AddComponent<WorldSaveState>(world);
            var go=tx.Empty("Игрок",world.transform,"player");go.transform.position=position;Place(go,"player_spawn",WorldPlaceKind.Spawn);
            var motor=Undo.AddComponent<CharacterController>(go);motor.height=1.8f;motor.radius=.35f;motor.center=new Vector3(0,.9f,0);Undo.AddComponent<Inventory>(go);
            var camera=tx.Empty("Камера игрока",go.transform,"camera");camera.transform.localPosition=new Vector3(0,2.5f,-4);camera.transform.localRotation=Quaternion.Euler(20,0,0);var view=Undo.AddComponent<Camera>(camera);view.depth=100;view.nearClipPlane=.15f;view.farClipPlane=2000;view.enabled=false;
            var player=Undo.AddComponent<WorldPlayer>(go);player.View=view;Anchor(go,world.GetComponentInChildren<Terrain>(),0);return go;
        }
        private static GameObject Quest(SceneBuildTransaction tx,GameObject world,GameObject giver,GameObject recipient,int reward)
        {
            if(giver==recipient||!giver.transform.IsChildOf(world.transform)||!recipient.transform.IsChildOf(world.transform))throw new ArgumentException("Quest needs two different NPCs in this world.");
            if(world.GetComponentInChildren<DeliveryQuest>(true))throw new ArgumentException("This world already has a delivery quest.");
            var g=giver.GetComponent<WorldPlace>()??Place(giver,"giver",WorldPlaceKind.Npc);var r=recipient.GetComponent<WorldPlace>()??Place(recipient,"guard",WorldPlaceKind.Npc);
            var go=tx.Empty("Письмо стражу",world.transform,"quest");var quest=Undo.AddComponent<DeliveryQuest>(go);quest.Giver=g;quest.Recipient=r;quest.RewardAmount=reward;
            if(!world.GetComponent<WorldSaveState>())Undo.AddComponent<WorldSaveState>(world);return go;
        }
        private static JArray Catalog(string query,int limit)
        {
            return new JArray(AssetDatabase.FindAssets("t:Prefab "+query,new[]{"Assets"}).Take(limit).Select(guid=>
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);var renderers=go.GetComponentsInChildren<Renderer>(true);var bounds=renderers.Length>0?renderers[0].bounds:new Bounds();foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                return new JObject { ["guid"]=guid,["path"]=path,["name"]=go.name,["renderers"]=renderers.Length,["colliders"]=go.GetComponentsInChildren<Collider>(true).Length,["scripts"]=new JArray(go.GetComponentsInChildren<MonoBehaviour>(true).Where(s=>s).Select(s=>s.GetType().FullName)),["size"]=new JArray(bounds.size.x,bounds.size.y,bounds.size.z),["materials"]=new JArray(renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Select(m=>m.name).Distinct()),["pipeline"]=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name:"Built-in",["license"]="User-supplied project asset; verify its own license" };
            }));
        }
        private static GameObject Instantiate(SceneBuildTransaction tx,Transform parent,string name,JObject args)
        {
            var prefab=Prefab(args);var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);Undo.RegisterCreatedObjectUndo(instance,"Instantiate reviewed prefab");instance.name=name;
            if(args["position"]!=null)instance.transform.position=Vector(args["position"]);if(args["scale"]!=null)instance.transform.localScale=Vector(args["scale"]);
            foreach(var t in instance.GetComponentsInChildren<Transform>(true)) { var own=t.GetComponent<GeneratedSceneObject>()??Undo.AddComponent<GeneratedSceneObject>(t.gameObject);own.Kind=t==instance.transform?"prefab":"part";own.BatchId=tx.BatchId;own.EnsureUniqueIdentity();tx.Objects.Add(t.gameObject); }
            foreach(var copiedPlace in instance.GetComponentsInChildren<WorldPlace>(true)) { Undo.RecordObject(copiedPlace,"Copy persistent identity");copiedPlace.PersistentId=Guid.NewGuid().ToString("N");var entity=copiedPlace.GetComponent<WorldEntity>();if(entity)entity.Id=copiedPlace.PersistentId; }
            Place(instance,"prefab_"+Guid.NewGuid().ToString("N"),WorldPlaceKind.House);return instance;
        }
        internal static JObject Report(GameObject world)
        {
            var nav=world.GetComponent<WorldNavigation>();var places=world.GetComponentsInChildren<WorldPlace>(true);var failures=nav?nav.ValidateConnections():new[]{"No baked navigation"};
            var missing=world.GetComponentsInChildren<MonoBehaviour>(true).Count(s=>!s);
            return new JObject { ["name"]=world.name,["places"]=new JArray(places.Select(p=>new JObject { ["id"]=p.PersistentId,["key"]=p.Key,["kind"]=p.Kind.ToString(),["connections"]=new JArray(p.Connections.Where(c=>c).Select(c=>c.Key)) })),["houses"]=places.Count(p=>p.Kind==WorldPlaceKind.House),["navigationFailures"]=failures.Length,["failures"]=new JArray(failures),["missingScripts"]=missing,["player"]=world.GetComponentInChildren<WorldPlayer>(true)!=null,["quest"]=world.GetComponentInChildren<DeliveryQuest>(true)!=null,["clock"]=world.GetComponent<WorldClock>()?.Snapshot(),["gates"]=new JArray(world.GetComponentsInChildren<WorldGate>(true).Select(g=>g.Snapshot())),["success"]=failures.Length==0&&missing==0 };
        }
    }
}
