using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Tracks only newly-created assets. A failed batch cannot delete or overwrite a consumer asset.</summary>
    internal sealed class SceneBuildTransaction
    {
        internal readonly string BatchId = Guid.NewGuid().ToString("N");
        internal readonly List<string> Assets = new List<string>();
        internal readonly List<string> Folders = new List<string>();
        internal readonly List<GameObject> Objects = new List<GameObject>();
        private string folder;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        internal string Folder
        {
            get
            {
                if (folder != null) return folder;
                string current = "Assets";
                foreach (string part in new[] { "WorldWeaverGenerated", "Assistant", BatchId })
                {
                    string next = current + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next)) { if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, part))) throw new InvalidOperationException("Cannot create asset folder " + next); Folders.Add(next); }
                    current = next;
                }
                return folder = current;
            }
        }
        internal T Asset<T>(T asset, string name, string extension = ".asset") where T : Object
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + SafeName(name) + extension);
            // Record before creation too, so a partially successful import is cleaned up.
            Assets.Add(path); AssetDatabase.CreateAsset(asset, path);
            if (!AssetDatabase.LoadAssetAtPath<T>(path)) throw new InvalidOperationException("Asset read-back failed: " + path);
            return asset;
        }
        internal static string SafeName(string name)
        { string s = new string((name ?? "Object").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray()); return s.Length == 0 ? "Object" : s.Substring(0, Math.Min(80, s.Length)); }
        internal GameObject Empty(string name, Transform parent, string kind, bool anchor = false)
        {
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "WorldWeaver assistant");
            if (parent) go.transform.SetParent(parent, false);
            var owned = Undo.AddComponent<GeneratedSceneObject>(go); owned.Kind = kind; owned.BatchId = BatchId; owned.SurfaceAnchored = anchor;
            Objects.Add(go); return go;
        }
        internal GameObject Primitive(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type); Undo.RegisterCreatedObjectUndo(go, "WorldWeaver assistant"); go.name = name;
            if (parent) go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            var owned = Undo.AddComponent<GeneratedSceneObject>(go); owned.Kind = "part"; owned.BatchId = BatchId;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            Objects.Add(go); return go;
        }
        internal Material Material(string key, Color color, float smoothness = .15f)
        {
            string cache = key + color.ToString() + smoothness;
            if (materials.TryGetValue(cache, out var existing)) return existing;
            bool urp = GraphicsSettings.currentRenderPipeline && GraphicsSettings.currentRenderPipeline.GetType().Name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
            Shader shader = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("No supported Lit shader found.");
            var m = new Material(shader) { name = key, color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            materials[cache] = Asset(m, key, ".mat"); return m;
        }
        internal Mesh Mesh(string key, Func<Mesh> create)
        { if (!meshes.TryGetValue(key, out var mesh)) meshes[key] = mesh = Asset(create(), key); return mesh; }
        internal void RollbackAssets()
        {
            var failures = new List<string>();
            foreach (string path in Assets.AsEnumerable().Reverse())
            {
                if (!(AssetDatabase.LoadMainAssetAtPath(path) || File.Exists(path))) continue;
                try { if (!AssetDatabase.DeleteAsset(path) || File.Exists(path)) failures.Add(path); }
                catch (Exception error) { failures.Add(path + ": " + error.Message); }
            }
            foreach (string path in Folders.AsEnumerable().Reverse())
                if (AssetDatabase.IsValidFolder(path) && AssetDatabase.FindAssets("", new[] { path }).Length == 0 && !AssetDatabase.DeleteAsset(path)) failures.Add(path);
            if(failures.Count > 0) throw new IOException("Rollback could not remove owned new assets: " + string.Join(", ", failures));
        }
    }

    internal static class WorldComposer
    {
        internal static TerrainSpec Specification(JObject args, string style, TerrainSpec original = null)
        {
            var spec = original == null ? new TerrainSpec() : JObject.FromObject(original).ToObject<TerrainSpec>();
            if (original == null)
            {
                spec.height = style == "alpine" ? 44 : style == "desert" ? 24 : 28;
                spec.mountainStrength = style == "alpine" ? .65f : .25f;
                spec.valleyDepth = style == "desert" ? .05f : .14f;
                spec.waterLevel = style == "desert" ? .1f : .23f;
            }
            var merged = JObject.FromObject(spec);
            foreach (var property in args.Properties()) if (merged.Property(property.Name) != null) merged[property.Name] = property.Value.DeepClone();
            return TerrainSpec.Parse(merged.ToString());
        }
        internal static GameObject World(SceneBuildTransaction tx, Transform group, string name, JObject args)
        {
            string style = (string)args["style"] ?? "forest"; var spec = Specification(args, style);
            var root = tx.Empty(name, group, "world"); root.GetComponent<GeneratedSceneObject>().Style = style;
            var data = tx.Asset(new TerrainData { heightmapResolution = spec.resolution, size = new Vector3(spec.width, spec.height, spec.length) }, "Terrain");
            data.SetHeights(0, 0, TerrainGenerator.Generate(spec));
            var go = Terrain.CreateTerrainGameObject(data); go.name = "Terrain"; Undo.RegisterCreatedObjectUndo(go, "WorldWeaver assistant"); go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(-spec.width / 2, 0, -spec.length / 2);
            var owned = Undo.AddComponent<GeneratedSceneObject>(go); owned.Kind = "terrain"; owned.BatchId = tx.BatchId; tx.Objects.Add(go);
            Undo.AddComponent<GeneratedTerrain>(go).Specification = spec;
            var terrain = go.GetComponent<Terrain>(); terrain.drawInstanced = true;
            bool urp = GraphicsSettings.currentRenderPipeline && GraphicsSettings.currentRenderPipeline.GetType().Name.IndexOf("Universal",StringComparison.OrdinalIgnoreCase)>=0;
            Shader terrainShader = Shader.Find(urp ? "Universal Render Pipeline/Terrain/Lit" : "Nature/Terrain/Standard");
            if (terrainShader) terrain.materialTemplate = tx.Asset(new Material(terrainShader), "TerrainMaterial", ".mat");
            Layers(tx, data, spec, style);
            Water(tx, root.transform, spec);
            var random = new System.Random(spec.seed); var positions = new List<Vector3>();
            int buildings = (int?)args["buildings"] ?? 4, trees = (int?)args["trees"] ?? (style == "desert" ? 12 : 40), rocks = (int?)args["rocks"] ?? 18;
            Populate(tx, root.transform, terrain, "house", buildings, random, positions, style);
            Populate(tx, root.transform, terrain, "tree", trees, random, positions, style);
            Populate(tx, root.transform, terrain, "rock", rocks, random, positions, style);
            var sun = tx.Empty("Sun", root.transform, "light"); var light = Undo.AddComponent<Light>(sun); light.type = LightType.Directional; light.intensity = 1.25f;
            light.color = style == "desert" ? new Color(1, .87f, .67f) : new Color(1, .96f, .88f); light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42, -32, 0);
            var camera = tx.Empty("World Camera", root.transform, "camera"); var cam = Undo.AddComponent<Camera>(camera);
            camera.transform.position = new Vector3(spec.width * .55f, spec.height + spec.width * .36f, -spec.length * .58f);
            camera.transform.LookAt(new Vector3(0, spec.height * .22f, 0)); cam.fieldOfView = 50; cam.nearClipPlane = .3f; cam.farClipPlane = 4000;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = style == "desert" ? new Color(.80f, .72f, .59f) : new Color(.49f, .70f, .84f);
            // Respect the consumer's existing active camera rather than stacking views.
            cam.enabled = !Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c => c != cam && c.enabled && c.gameObject.scene == root.scene);
            if(cam.enabled && !Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(l=>l.enabled && l.gameObject.scene==root.scene)) Undo.AddComponent<AudioListener>(camera);
            return root;
        }
        internal static void Water(SceneBuildTransaction tx, Transform root, TerrainSpec spec)
        {
            tx.Primitive("Water", PrimitiveType.Plane, root, tx.Material("Lake", new Color(.075f, .34f, .45f), .86f), new Vector3(0, spec.waterLevel * spec.height, 0), new Vector3(spec.width / 10, 1, spec.length / 10), false);
        }
        internal static void Layers(SceneBuildTransaction tx, TerrainData data, TerrainSpec spec, string style)
        {
            Color[] colors = style == "desert" ? new[] { new Color(.76f,.57f,.31f), new Color(.50f,.32f,.19f), new Color(.91f,.77f,.50f) } :
                new[] { new Color(.23f,.38f,.20f), new Color(.40f,.43f,.40f), new Color(.88f,.91f,.91f) };
            var layers = new TerrainLayer[3];
            for (int i = 0; i < 3; i++)
            {
                var texture = new Texture2D(32,32) { name = "Ground " + i, wrapMode = TextureWrapMode.Repeat }; var pixels = new Color[1024];
                var random = new System.Random(spec.seed + i * 31);
                for (int p = 0; p < pixels.Length; p++) pixels[p] = colors[i] * (.9f + (float)random.NextDouble() * .2f);
                texture.SetPixels(pixels); texture.Apply(); tx.Asset(texture, "GroundTexture" + i);
                layers[i] = tx.Asset(new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(14,14) }, "GroundLayer" + i, ".terrainlayer");
            }
            data.terrainLayers = layers; Alpha(data,spec);
        }
        internal static void Alpha(TerrainData data, TerrainSpec spec)
        {
            int n = 64; data.alphamapResolution = n; var values = new float[n,n,3];
            for (int z=0;z<n;z++) for(int x=0;x<n;x++)
            {
                float h = data.GetInterpolatedHeight(x/(float)(n-1),z/(float)(n-1))/spec.height;
                float slope = data.GetSteepness(x/(float)(n-1),z/(float)(n-1));
                float snow = Mathf.Clamp01((h-.68f)*6), rock = Mathf.Max(Mathf.Clamp01((h-.38f)*2),Mathf.Clamp01((slope-24)/35))*(1-snow);
                values[z,x,0]=1-rock-snow;values[z,x,1]=rock;values[z,x,2]=snow;
            }
            data.SetAlphamaps(0,0,values);
        }
        internal static void Populate(SceneBuildTransaction tx, Transform parent, Terrain terrain, string kind, int count, System.Random random, List<Vector3> occupied, string style, Vector3? center = null, Vector2? size = null, float spacing = 0)
        {
            float width = size?.x ?? terrain.terrainData.size.x * .88f, length = size?.y ?? terrain.terrainData.size.z * .88f;
            Vector3 middle = center ?? (terrain.transform.position + terrain.terrainData.size * .5f);
            float gap = spacing > 0 ? spacing : kind == "house" ? 11 : kind == "tree" ? 3.5f : 1.6f;
            int made=0, attempts=0;
            var spec = terrain.GetComponent<GeneratedTerrain>().Specification;
            while(made<count && attempts++ < Math.Max(200,count*180))
            {
                float x = middle.x + ((float)random.NextDouble()-.5f)*width, z=middle.z+((float)random.NextDouble()-.5f)*length;
                Vector3 p = Ground(terrain,new Vector3(x,0,z));
                float u=(x-terrain.transform.position.x)/terrain.terrainData.size.x,v=(z-terrain.transform.position.z)/terrain.terrainData.size.z;
                if(u<.02f||u>.98f||v<.02f||v>.98f||p.y < terrain.transform.position.y+spec.waterLevel*spec.height+.8f) continue;
                if(terrain.terrainData.GetSteepness(u,v) > (kind=="house" ? 18 : kind=="tree" ? 34 : 46)) continue;
                if(occupied.Any(q => new Vector2(q.x-p.x,q.z-p.z).magnitude < gap)) continue;
                var go=ObjectOfKind(tx,parent,kind+" "+(made+1),kind,style);go.transform.position=p;
                go.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                float scale=kind=="house" ? .85f+(float)random.NextDouble()*.25f : .7f+(float)random.NextDouble()*.65f;go.transform.localScale=Vector3.one*scale;
                go.transform.position=SceneToolExecutor.GroundPoint(go,terrain,p);occupied.Add(p);made++;
            }
            if(made!=count) throw new InvalidOperationException("Not enough dry, sufficiently level space for " + count + " " + kind + " objects; reduce count or spacing.");
        }
        internal static Vector3 Ground(Terrain terrain, Vector3 p)
        { p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p; }
        internal static GameObject ObjectOfKind(SceneBuildTransaction tx, Transform parent, string name, string kind, string style = "forest")
        {
            if(kind=="tree")
            {
                var root=tx.Empty(name,parent,"tree",true);
                tx.Primitive("Trunk",PrimitiveType.Cylinder,root.transform,tx.Material("Bark",new Color(.29f,.19f,.11f)),new Vector3(0,1.6f,0),new Vector3(.34f,1.6f,.34f));
                Color leaves=style=="desert" ? new Color(.34f,.43f,.18f) : new Color(.11f,.29f,.18f);
                MeshPart(tx,"Lower crown",root.transform,tx.Mesh("PineCone",Cone),tx.Material("Foliage",leaves),new Vector3(0,2.15f,0),new Vector3(3.8f,3.5f,3.8f));
                MeshPart(tx,"Upper crown",root.transform,tx.Mesh("PineCone",Cone),tx.Material("FoliageLight",leaves*1.13f),new Vector3(0,4.05f,0),new Vector3(2.8f,2.7f,2.8f));return root;
            }
            if(kind=="house")
            {
                var root=tx.Empty(name,parent,"house",true);
                tx.Primitive("Foundation",PrimitiveType.Cube,root.transform,tx.Material("Stone",new Color(.36f,.39f,.38f)),new Vector3(0,.15f,0),new Vector3(5.4f,.65f,4.4f));
                tx.Primitive("Walls",PrimitiveType.Cube,root.transform,tx.Material("Plaster",new Color(.82f,.72f,.54f)),new Vector3(0,1.8f,0),new Vector3(5,3,4));
                MeshPart(tx,"Gabled roof",root.transform,tx.Mesh("GabledRoof",Roof),tx.Material("Roof",style=="desert" ? new Color(.47f,.26f,.16f) : new Color(.27f,.20f,.19f)),new Vector3(0,3.3f,0),new Vector3(5.8f,1.8f,4.8f));
                tx.Primitive("Door",PrimitiveType.Cube,root.transform,tx.Material("DoorWood",new Color(.30f,.19f,.10f)),new Vector3(0,1.23f,-2.03f),new Vector3(.95f,1.85f,.12f));
                var glass=tx.Material("Window",new Color(.29f,.59f,.64f),.7f);
                foreach(float x in new[]{-1.5f,1.5f})tx.Primitive("Front window",PrimitiveType.Cube,root.transform,glass,new Vector3(x,2,-2.05f),new Vector3(.85f,.85f,.12f));
                foreach(float x in new[]{-2.53f,2.53f})tx.Primitive("Side window",PrimitiveType.Cube,root.transform,glass,new Vector3(x,2,0),new Vector3(.12f,.85f,.85f));return root;
            }
            PrimitiveType type=kind=="sphere"||kind=="rock" ? PrimitiveType.Sphere : kind=="capsule" ? PrimitiveType.Capsule : kind=="cylinder" ? PrimitiveType.Cylinder : kind=="plane" ? PrimitiveType.Plane : PrimitiveType.Cube;
            var obj=tx.Primitive(name,type,parent,tx.Material(kind=="rock" ? "Rock" : "Object",kind=="rock" ? new Color(.38f,.42f,.41f) : new Color(.57f,.65f,.68f)),Vector3.zero,kind=="rock" ? new Vector3(2.2f,1.5f,1.8f) : Vector3.one);
            obj.GetComponent<GeneratedSceneObject>().Kind=kind;obj.GetComponent<GeneratedSceneObject>().SurfaceAnchored=true;
            // Primitive roots use ground as a foot position.
            if(kind!="plane") { var filter=obj.GetComponent<MeshFilter>(); if(filter && filter.sharedMesh) obj.transform.position += Vector3.up * filter.sharedMesh.bounds.extents.y * obj.transform.localScale.y; }
            return obj;
        }
        private static void MeshPart(SceneBuildTransaction tx,string name,Transform parent,Mesh mesh,Material material,Vector3 p,Vector3 scale)
        { var go=tx.Empty(name,parent,"part");go.transform.localPosition=p;go.transform.localScale=scale;Undo.AddComponent<MeshFilter>(go).sharedMesh=mesh;Undo.AddComponent<MeshRenderer>(go).sharedMaterial=material; }
        private static Mesh Cone()
        {
            const int sides=9;var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;int j=vertices.Count;vertices.Add(new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f));vertices.Add(Vector3.up);vertices.Add(new Vector3(Mathf.Cos(b)*.5f,0,Mathf.Sin(b)*.5f));triangles.AddRange(new[]{j,j+1,j+2});}
            var mesh=new Mesh{name="Low polygon pine crown"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Roof()
        {
            var points=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(0,1,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(0,1,.5f)};
            int[] indices={0,2,1,3,4,5,0,3,5,0,5,2,1,2,5,1,5,4,0,1,4,0,4,3};
            var vertices=new Vector3[indices.Length];for(int i=0;i<indices.Length;i++)vertices[i]=points[indices[i]];
            var mesh=new Mesh{name="Gabled roof"};mesh.vertices=vertices;mesh.triangles=Enumerable.Range(0,vertices.Length).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        internal static GameObject Npc(SceneBuildTransaction tx,Transform parent,string name,string role)
        {
            var root=tx.Empty(name,parent,"npc",true);Color cloth=role.ToLowerInvariant().Contains("guard")||role.ToLowerInvariant().Contains("страж") ? new Color(.22f,.33f,.50f) : new Color(.52f,.24f,.16f);
            tx.Primitive("Coat",PrimitiveType.Capsule,root.transform,tx.Material("Coat",cloth),new Vector3(0,.82f,0),new Vector3(.65f,.72f,.65f));
            tx.Primitive("Head",PrimitiveType.Sphere,root.transform,tx.Material("Skin",new Color(.78f,.58f,.40f)),new Vector3(0,1.73f,0),Vector3.one*.52f,false);
            tx.Primitive("Hat brim",PrimitiveType.Cylinder,root.transform,tx.Material("Hat",new Color(.20f,.17f,.13f)),new Vector3(0,2,0),new Vector3(.74f,.045f,.74f),false);
            tx.Primitive("Hat crown",PrimitiveType.Cylinder,root.transform,tx.Material("Hat",new Color(.20f,.17f,.13f)),new Vector3(0,2.14f,0),new Vector3(.43f,.15f,.43f),false);
            foreach(float x in new[]{-.1f,.1f})tx.Primitive("Eye",PrimitiveType.Sphere,root.transform,tx.Material("Eyes",new Color(.08f,.09f,.09f)),new Vector3(x,1.79f,.23f),Vector3.one*.065f,false);
            tx.Primitive("Satchel",PrimitiveType.Cube,root.transform,tx.Material("Leather",new Color(.30f,.21f,.12f)),new Vector3(.38f,.78f,0),new Vector3(.25f,.42f,.35f),false);
            return root;
        }
    }
}
