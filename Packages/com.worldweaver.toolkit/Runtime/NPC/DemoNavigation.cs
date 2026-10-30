using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>Builds a small demonstration NavMesh on explicitly assigned walkable meshes.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DemoNavigation : MonoBehaviour
    {
        public MeshFilter[] Surfaces;
        private NavMeshData data;
        private NavMeshDataInstance instance;
        private void OnEnable()
        {
            var sources = new List<NavMeshBuildSource>();
            foreach (var surface in Surfaces) if (surface)
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = surface.sharedMesh, transform = surface.transform.localToWorldMatrix, area = 0 });
            var settings = NavMesh.GetSettingsByIndex(0);
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(28, 14, -25), new Vector3(42, 12, 35)), Vector3.zero, Quaternion.identity);
            if (data) instance = NavMesh.AddNavMeshData(data);
        }
        private void OnDisable() { if (instance.valid) instance.Remove(); if (data) Destroy(data); }
    }
}
