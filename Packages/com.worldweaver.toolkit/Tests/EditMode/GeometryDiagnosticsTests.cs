using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class GeometryDiagnosticsTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        readonly List<Mesh> meshes=new List<Mesh>();
        bool backfaces;
        [SetUp] public void Setup(){backfaces=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=false;}
        [TearDown] public void Cleanup(){foreach(var go in objects)if(go)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);objects.Clear();meshes.Clear();Physics.queriesHitBackfaces=backfaces;}
        Mesh Plane(bool down=false)
        {
            var mesh=new Mesh{name="Diagnostic plane"};meshes.Add(mesh);
            mesh.vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)};
            mesh.triangles=down?new[]{0,2,1,0,3,2}:new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        GameObject Object(Mesh mesh,bool collision=true)
        {
            var go=new GameObject("Geometry diagnostic fixture");objects.Add(go);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>();if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
        }
        static JObject Direction(JObject result,string axis)=>((JArray)result["meshColliders"][0]["directionalCoverage"]["byDirection"]).OfType<JObject>().Single(d=>(string)d["fromDirection"]==axis);

        [Test] public void FlippedSurfaceExposesBackfaceOnlyGeometryAndRealPhysicalMissWithoutRepair()
        {
            var mesh=Plane(true);var go=Object(mesh);var before=mesh.triangles.ToArray();var transform=go.transform.localToWorldMatrix;
            var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());var above=Direction(result,"+Y");
            Assert.That((bool)result["analysisAvailable"],Is.True);Assert.That((int)result["worldFaceNormalDirections"]["-Y"],Is.EqualTo(2));
            Assert.That((int)above["twoSidedGeometryHits"],Is.EqualTo(9));Assert.That((int)above["frontFacingGeometryHits"],Is.Zero);Assert.That((int)above["colliderHits"],Is.Zero);
            CollectionAssert.AreEqual(before,mesh.triangles);Assert.That(go.transform.localToWorldMatrix,Is.EqualTo(transform));Assert.That(Physics.queriesHitBackfaces,Is.False);Assert.That((bool)result["readOnly"],Is.True);
        }
        [Test] public void CorrectSurfaceReportsFrontFacingAndActualCoverageFromAbove()
        {
            var go=Object(Plane());var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshCollider>());var above=Direction(result,"+Y");
            Assert.That((int)above["twoSidedGeometryHits"],Is.EqualTo(9));Assert.That((int)above["frontFacingGeometryHits"],Is.EqualTo(9));Assert.That((int)above["colliderHits"],Is.EqualTo(9));
            Assert.That((int)Direction(result,"-Y")["frontFacingGeometryHits"],Is.Zero);Assert.That((int)result["sampledTriangles"],Is.EqualTo(2));
        }
        [Test] public void GeometryOrientationIsMeasuredAfterMirroredTransform()
        {
            var go=Object(Plane());go.transform.localScale=new Vector3(-1,1,1);var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((float)result["transformDeterminant"],Is.LessThan(0));Assert.That((int)result["worldFaceNormalDirections"]["-Y"],Is.EqualTo(2));Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("mirrors")),Is.True);
        }
        [Test] public void DegenerateTrianglesAreCountedInsteadOfClaimingCollisionCoverage()
        {
            var mesh=Plane();mesh.triangles=new[]{0,0,1,0,1,2};var go=Object(mesh,false);var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((int)result["degenerateTrianglesInSample"],Is.EqualTo(1));Assert.That((int)result["sampledTriangles"],Is.EqualTo(2));Assert.That(((JArray)result["meshColliders"]).Count,Is.Zero);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("No MeshCollider")),Is.True);
        }
        [Test] public void DifferentCollisionMeshIsExplicitlyIdentified()
        {
            var go=Object(Plane());go.GetComponent<MeshCollider>().sharedMesh=Plane(true);var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["meshColliders"][0]["matchesInspectedMesh"],Is.False);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("different mesh")),Is.True);
        }
        [Test] public void UnreadableMeshProducesBoundedFactsWithoutAttemptingVertexRead()
        {
            var mesh=Plane();var go=Object(mesh,false);mesh.UploadMeshData(true);var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["analysisAvailable"],Is.False);Assert.That((int)result["mesh"]["vertices"],Is.EqualTo(4));Assert.That(result["triangleSamples"],Is.Null);
        }
        [Test] public void OversizedMeshDoesNotAllocateUnboundedTriangleAnalysis()
        {
            var mesh=new Mesh{name="Large diagnostic input",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};meshes.Add(mesh);mesh.vertices=new Vector3[65537];var go=Object(mesh,false);var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["analysisAvailable"],Is.False);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("bounded inspection size")),Is.True);
        }
        [Test] public void GlobalBackfaceQueryFlagIsObservedAndNeverChangedByInspection()
        {
            Physics.queriesHitBackfaces=true;var go=Object(Plane(true));var result=GeometryDiagnostics.Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["queriesHitBackfaces"],Is.True);Assert.That(Physics.queriesHitBackfaces,Is.True);
        }
        [Test] public void ComponentInspectionExposesDiagnosticsWhileOrdinaryComponentsStaySmall()
        {
            var go=Object(Plane());var mesh=AgentProjectTools.InspectComponent(go.GetComponent<MeshFilter>());var transform=AgentProjectTools.InspectComponent(go.transform);
            Assert.That(mesh["geometryDiagnostics"]["analysisAvailable"].Value<bool>(),Is.True);Assert.That(transform["geometryDiagnostics"],Is.Null);Assert.That(mesh["fields"],Is.Not.Null);
        }
        [Test] public void CompactSnapshotDoesNotPerformTriangleAnalysis()
        {
            var go=Object(Plane());var summary=GeometryDiagnostics.Summary(go);
            Assert.That((int)summary["visualMesh"]["vertices"],Is.EqualTo(4));Assert.That(summary["triangleSamples"],Is.Null);StringAssert.Contains("inspect_component",(string)summary["detailTool"]);
        }
    }
}
