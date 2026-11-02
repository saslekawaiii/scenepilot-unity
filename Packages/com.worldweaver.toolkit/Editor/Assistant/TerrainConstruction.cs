using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    internal static class TerrainConstruction
    {
        static Terrain TerrainOf(GameObject go)=>go.GetComponentInChildren<Terrain>();
        internal static void Validate(EditorCommand c,GameObject target)
        {
            var terrain=TerrainOf(target);
            if(!terrain||!terrain.GetComponent<GeneratedTerrain>())throw new ArgumentException("An owned generated Terrain is required.");
            if(c.tool=="shape_terrain"&&(string)c.args["operation"]!="refine"&&terrain.transform.parent.GetComponentsInChildren<WorldCave>().Length>0)throw new ArgumentException("This terrain has caves. Sculpt before cave creation so entrances and collision stay aligned.");
            if(c.tool=="create_cave")
            {
                float x=(float)c.args["position"][0],z=(float)c.args["position"][1],width=(float?)c.args["width"]??7,length=(float?)c.args["length"]??24;
                var start=terrain.transform.position;var size=terrain.terrainData.size;
                int needed=RequiredResolution(terrain,width);
                if(needed>2049)throw new ArgumentException("Cave needs a finer terrain grid than the supported 2049 resolution; choose a wider entrance or a smaller world.");
                if(x-width<start.x||x+width>start.x+size.x||z-2<start.z||z+length+2>start.z+size.z)throw new ArgumentException("Cave and approach must fit inside terrain.");
                if(!WorldSitePlacement.Clear(terrain,WorldSitePlacement.CaveFootprint(new Vector3(x,0,z),width,length),null,out var obstruction,false))throw new ArgumentException("Cave approach/footprint is occupied by "+obstruction+". Explicit coordinates were not changed; inspect the scene and choose a clear site.");
                foreach(var cave in terrain.transform.parent.GetComponentsInChildren<WorldCave>())if(Mathf.Abs(cave.transform.position.x-x)<width+cave.Size.x&&Mathf.Abs(cave.transform.position.z-z)<length+cave.Size.z)throw new ArgumentException("Cave footprints overlap.");
            }
        }
        internal static int RequiredResolution(Terrain terrain,float width)=>Mathf.Max(terrain.terrainData.heightmapResolution,Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(terrain.terrainData.size.x,terrain.terrainData.size.z)/(width/4)))+1);
        internal static void ReanchorCaves(Terrain terrain)
        {
            var caves=terrain.transform.parent.GetComponentsInChildren<WorldCave>().Where(c=>c.Terrain==terrain).ToArray();
            if(caves.Length==0)return;
            int n=terrain.terrainData.holesResolution;var holes=new bool[n,n];for(int z=0;z<n;z++)for(int x=0;x<n;x++)holes[z,x]=true;
            var origin=terrain.transform.position;var size=terrain.terrainData.size;
            foreach(var cave in caves)
            {
                var p=cave.transform.position;
                if(p.x-cave.Size.x/2<origin.x||p.x+cave.Size.x/2>origin.x+size.x||p.z<origin.z||p.z+cave.Size.z>origin.z+size.z)throw new ArgumentException("Resized terrain would exclude an existing cave. Choose larger extents or relocate the cave first.");
                float delta=terrain.SampleHeight(p)+origin.y-p.y;
                Undo.RecordObject(cave.transform,"Reanchor cave after terrain edit");cave.transform.position+=Vector3.up*delta;
                Undo.RecordObject(cave,"Reanchor cave navigation points");cave.Entrance+=Vector3.up*delta;cave.Interior+=Vector3.up*delta;
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    float px=origin.x+(x+.5f)/n*size.x,pz=origin.z+(z+.5f)/n*size.z;
                    if(px>p.x-cave.Size.x/2&&px<p.x+cave.Size.x/2&&pz>p.z&&pz<p.z+cave.Size.z)holes[z,x]=false;
                }
            }
            terrain.terrainData.SetHoles(0,0,holes);
        }
        static TerrainData Clone(SceneBuildTransaction tx,Terrain terrain)
        {
            var data=tx.Asset(Object.Instantiate(terrain.terrainData),"Edited terrain");Undo.RecordObject(terrain,"Assign owned terrain data");var collider=terrain.GetComponent<TerrainCollider>();Undo.RecordObject(collider,"Assign terrain collision data");terrain.terrainData=data;collider.terrainData=data;return data;
        }
        internal static JObject Shape(EditorCommand c,SceneBuildTransaction tx,GameObject target)
        {
            var terrain=TerrainOf(target);var data=Clone(tx,terrain);var a=c.args;string operation=(string)a["operation"];var generated=terrain.GetComponent<GeneratedTerrain>();
            var env=terrain.transform.parent.GetComponent<WorldEnvironment>()??Undo.AddComponent<WorldEnvironment>(terrain.transform.parent.gameObject);Undo.RecordObject(env,"Record terrain features");env.Terrain=terrain;
            int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);
            if(operation=="refine")return Refine(c,terrain,data,generated);
            if(operation=="lunar")
            {
                var spec=JObject.FromObject(generated.Specification).ToObject<TerrainSpec>();spec.seed=(int?)a["seed"]??spec.seed;
                env.Craters=ExplorationTerrain.Craters(spec.seed,(int?)a["craters"]??8,spec.width,spec.length);
                heights=ExplorationTerrain.Generate(spec,env.Craters,true);env.Profile="moon";
                WorldComposer.Layers(tx,data,spec,"moon");
            }
            else
            {
                float cx=a["center"]!=null?(float)a["center"][0]:0,cz=a["center"]!=null?(float)a["center"][1]:0;
                float radius=(float?)a["radius"]??8,amount=(float?)a["amount"]??3;if(operation=="crater")amount=Mathf.Abs(amount);if(Mathf.Abs(amount)<.1f)throw new ArgumentException("Brush amount magnitude must be 0.1..30 metres; crater depth accepts either sign.");
                float level=(terrain.SampleHeight(new Vector3(cx,0,cz)))/data.size.y;
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    var p=new Vector2(terrain.transform.position.x+x/(float)(n-1)*data.size.x,terrain.transform.position.z+z/(float)(n-1)*data.size.z);
                    float r=Vector2.Distance(p,new Vector2(cx,cz))/radius;
                    if(operation=="flatten"&&r<1)heights[z,x]=Mathf.Lerp(heights[z,x],level,Mathf.SmoothStep(1,0,r));
                    else if(operation=="hill"&&r<1)heights[z,x]=Mathf.Clamp01(heights[z,x]+amount/data.size.y*Mathf.Pow(1-r*r,2));
                    else if(operation=="crater")heights[z,x]=Mathf.Clamp01(heights[z,x]+amount/data.size.y*((r<1?-Mathf.Pow(1-r*r,2):0)+.35f*Mathf.Exp(-Mathf.Pow((r-1)/.16f,2))));
                }
                if(operation=="crater")env.Craters.Add(new ImpactCrater{Center=new Vector2(cx,cz),Radius=radius,Depth=amount});
            }
            data.SetHeights(0,0,heights);WorldComposer.Alpha(data,generated.Specification);WorldGameplayTools.RevalidateAfterTerrainUpdate(tx,target);EditorUtility.SetDirty(data);
            return new JObject{["operation"]=operation,["heightmapResolution"]=n,["craters"]=env.Craters.Count,["appliedDepthMetres"]=operation=="crater"?Mathf.Abs((float?)a["amount"]??3):0,["source"]=env.HeightmapSource,["terrain"]=ProjectContextIndex.StableTarget(terrain.gameObject)};
        }
        static JObject Refine(EditorCommand c,Terrain terrain,TerrainData data,GeneratedTerrain generated)
        {
            int old=data.heightmapResolution,n=(int?)c.args["resolution"]??Mathf.Min(513,(old-1)*2+1);Vector3 originalSize=data.size;
            if(n<old)throw new ArgumentException("Refine cannot discard existing heightmap detail; choose an equal or higher resolution.");
            var samples=new float[n,n];for(int z=0;z<n;z++)for(int x=0;x<n;x++)samples[z,x]=data.GetInterpolatedHeight(x/(float)(n-1),z/(float)(n-1))/data.size.y;
            int hn=data.holesResolution;var previousHoles=data.GetHoles(0,0,hn,hn);var holes=new bool[n-1,n-1];
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)holes[z,x]=previousHoles[Mathf.Min(hn-1,Mathf.FloorToInt((z+.5f)/(n-1)*hn)),Mathf.Min(hn-1,Mathf.FloorToInt((x+.5f)/(n-1)*hn))];
            data.heightmapResolution=n;data.size=originalSize;data.SetHeights(0,0,samples);data.SetHoles(0,0,holes);
            Undo.RecordObject(generated,"Refine owned terrain resolution");var spec=JObject.FromObject(generated.Specification).ToObject<TerrainSpec>();spec.resolution=n;generated.Specification=spec;EditorUtility.SetDirty(generated);EditorUtility.SetDirty(data);
            return new JObject{["operation"]="refine",["previousResolution"]=old,["heightmapResolution"]=n,["preservedHeights"]=true,["preservedHoles"]=true};
        }
        internal static GameObject Cave(EditorCommand c,SceneBuildTransaction tx,GameObject target)
        {
            var terrain=TerrainOf(target);float width=(float?)c.args["width"]??7,length=(float?)c.args["length"]??24;
            int needed=RequiredResolution(terrain,width);
            if(needed>terrain.terrainData.heightmapResolution)
            {
                var refine=new EditorCommand{tool="shape_terrain",target=c.target,args=new JObject{["operation"]="refine",["resolution"]=needed}};
                Shape(refine,tx,target);
            }
            var data=Clone(tx,terrain);
            var at=new Vector3((float)c.args["position"][0],0,(float)c.args["position"][1]);at.y=terrain.SampleHeight(at)+terrain.transform.position.y;
            var go=tx.Empty(c.name,terrain.transform.parent,"cave");go.transform.position=at;
            // A rectangular physical cut removes Terrain from the ramp/tube, including the approach.
            // The generated closed floor/walls/ceiling provide replacement collision and cover all cut edges.
            int n=data.holesResolution;var holes=data.GetHoles(0,0,n,n);var origin=terrain.transform.position;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float px=origin.x+(x+.5f)/n*data.size.x,pz=origin.z+(z+.5f)/n*data.size.z;
                if(px>at.x-width/2&&px<at.x+width/2&&pz>at.z&&pz<at.z+length)holes[z,x]=false;
            }
            data.SetHoles(0,0,holes);
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            void Quad(Vector3 a,Vector3 b,Vector3 cc,Vector3 d)
            {int i=vertices.Count;vertices.AddRange(new[]{a,b,cc,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3,i+2,i+1,i,i+3,i+2,i});}
            float half=width/2+.6f;
            // Continuous shallow ramp: 3 m descent over 12 m, then a level, walkable chamber.
            float Floor(float z)=>-3*Mathf.Clamp01(z/12);
            for(int segment=0;segment<12;segment++)
            {
                float z0=length*segment/12,z1=length*(segment+1)/12,y0=Floor(z0),y1=Floor(z1);
                Quad(new Vector3(-half,y0,z0),new Vector3(-half,y1,z1),new Vector3(half,y1,z1),new Vector3(half,y0,z0));
                Quad(new Vector3(-half,y0,z0),new Vector3(-half,y0+5,z0),new Vector3(-half,y1+5,z1),new Vector3(-half,y1,z1));
                Quad(new Vector3(half,y0,z0),new Vector3(half,y1,z1),new Vector3(half,y1+5,z1),new Vector3(half,y0+5,z0));
                if(segment>=2)
                {
                    for(int arch=0;arch<8;arch++)
                    {
                        float a0=arch*Mathf.PI/8,a1=(arch+1)*Mathf.PI/8;
                        Vector3 Roof(float a,float z,float y)=>new Vector3(-Mathf.Cos(a)*half,y+2+Mathf.Sin(a)*3,z);
                        Quad(Roof(a0,z0,y0),Roof(a0,z1,y1),Roof(a1,z1,y1),Roof(a1,z0,y0));
                    }
                }
            }
            Quad(new Vector3(-half,Floor(length),length),new Vector3(-half,Floor(length)+5,length),new Vector3(half,Floor(length)+5,length),new Vector3(half,Floor(length),length));
            var mesh=tx.Asset(new Mesh{name="Walkable cave",vertices=vertices.ToArray(),triangles=triangles.ToArray(),uv=uv.ToArray()},"Cave geometry");mesh.RecalculateNormals();mesh.RecalculateBounds();
            Undo.AddComponent<MeshFilter>(go).sharedMesh=mesh;Undo.AddComponent<MeshRenderer>(go).sharedMaterial=tx.Material("Cave stone",new Color(.32f,.34f,.37f));Undo.AddComponent<MeshCollider>(go).sharedMesh=mesh;
            var caveState=Undo.AddComponent<WorldCave>(go);caveState.Terrain=terrain;caveState.Size=new Vector3(width,5,length);caveState.Entrance=at+new Vector3(0,.1f,-1);caveState.Interior=at+new Vector3(0,-2.9f,length*.7f);
            var lamp=tx.Empty("Cave beacon",go.transform,"light");lamp.transform.localPosition=new Vector3(0,0,length*.6f);var light=Undo.AddComponent<Light>(lamp);light.type=LightType.Point;light.color=new Color(.4f,.65f,1);light.intensity=2;light.range=12;
            EditorUtility.SetDirty(data);return go;
        }
    }
}
