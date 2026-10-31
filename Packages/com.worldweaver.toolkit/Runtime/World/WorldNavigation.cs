using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>Owns an actual Unity NavMesh built from this world's colliders.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-1000)]
    public sealed class WorldNavigation : MonoBehaviour
    {
        public NavMeshData BakedData;
        public bool RebuildOnPlay = true;
        public string LastError { get; private set; }
        public int SourceCount { get; private set; }
        private NavMeshDataInstance instance;
        private NavMeshData runtimeData;
        private void OnEnable() { if (Application.isPlaying && RebuildOnPlay) Rebuild(); else Register(BakedData); }
        private void OnDisable() { if (instance.valid) instance.Remove(); if (runtimeData) Destroy(runtimeData); runtimeData = null; }
        public NavMeshData Build()
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            if (settings.agentTypeID < 0) throw new InvalidOperationException("No Unity NavMesh agent settings.");
            settings.agentRadius = .35f; settings.agentHeight = 1.8f; settings.agentClimb = .45f; settings.agentSlope = 38;
            var marks = new List<NavMeshBuildMarkup>();
            foreach (var npc in GetComponentsInChildren<IntelligentNPC>(true)) marks.Add(new NavMeshBuildMarkup { root = npc.transform, ignoreFromBuild = true });
            foreach (var player in GetComponentsInChildren<WorldPlayer>(true)) marks.Add(new NavMeshBuildMarkup { root = player.transform, ignoreFromBuild = true });
            foreach (var obstacle in GetComponentsInChildren<NavMeshObstacle>(true)) marks.Add(new NavMeshBuildMarkup { root=obstacle.transform,ignoreFromBuild=true });
            foreach (var own in GetComponentsInChildren<GeneratedSceneObject>(true)) if (own.name == "Water") marks.Add(new NavMeshBuildMarkup { root = own.transform, ignoreFromBuild = true });
            Physics.SyncTransforms();
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, marks, sources);
            SourceCount = sources.Count;
            var colliders = GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            if (sources.Count == 0 || colliders.Length == 0) throw new InvalidOperationException("World has no walkable colliders.");
            var bounds = colliders[0].bounds;
            foreach (var collider in colliders.Skip(1)) bounds.Encapsulate(collider.bounds);
            bounds.Expand(new Vector3(4,8,4));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (!data) throw new InvalidOperationException("Unity could not build this world's NavMesh.");
            return data;
        }
        public bool Rebuild()
        {
            try { var data = Build(); if (instance.valid) instance.Remove(); if (runtimeData) Destroy(runtimeData); runtimeData = data; Register(data); LastError = null; return true; }
            catch (Exception error) { LastError = error.Message; return false; }
        }
        public void Register(NavMeshData data) { if (instance.valid) instance.Remove(); if (data) instance = NavMesh.AddNavMeshData(data); }
        public WorldPlace Find(string key)
        {
            var found = GetComponentsInChildren<WorldPlace>(true).Where(p => p.Key == key || p.PersistentId == key || p.name == key).ToArray();
            if (found.Length != 1) throw new ArgumentException("Navigation place must exist exactly once: " + key);
            return found[0];
        }
        public static bool TryPath(Vector3 from, Vector3 to, out NavMeshPath path, float sampleDistance = 2)
        {
            path = new NavMeshPath();
            return NavMesh.SamplePosition(from,out var start,sampleDistance,NavMesh.AllAreas) && NavMesh.SamplePosition(to,out var end,sampleDistance,NavMesh.AllAreas) && NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) && path.status == NavMeshPathStatus.PathComplete;
        }
        private bool ExpectedBlocked(WorldPlace from,WorldPlace to) => GetComponentsInChildren<WorldGate>(true).Any(g=>!g.IsOpen&&((g.Inside==from&&g.Outside==to)||(g.Inside==to&&g.Outside==from)));
        public string[] ValidateConnections()
        {
            var errors = new List<string>();
            foreach (var place in GetComponentsInChildren<WorldPlace>(true)) foreach (var other in place.Connections.Where(p => p)) if (!ExpectedBlocked(place,other) && !TryPath(place.ArrivalPosition,other.ArrivalPosition,out _)) errors.Add(place.Key + " -> " + other.Key + ": unreachable");
            return errors.ToArray();
        }
    }
}
