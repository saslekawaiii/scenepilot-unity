using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class GeometryDiagnosticsTests
    {
        static JObject Call(string type,string method,object value)
        {
            var implementation=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant."+type,true);
            return (JObject)implementation.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{value});
        }
        static JArray Review(IEnumerable<GameObject> sceneObjects)
        {
            var implementation=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.GeometryDiagnostics",true);
            return (JArray)implementation.GetMethod("ReviewSceneEvidence",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{sceneObjects});
        }
        static JObject Inspect(Component component)=>Call("GeometryDiagnostics","Inspect",component);
        static JObject Snapshot(GameObject go)=>Call("GeometryDiagnostics","Summary",go);
        static JObject InspectComponent(Component component)=>Call("AgentProjectTools","InspectComponent",component);
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
        Mesh Tetrahedron(bool inward=false)
        {
            var mesh=new Mesh{name="Closed diagnostic shell"};meshes.Add(mesh);mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};
            var triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};if(inward)for(int i=0;i<triangles.Length;i+=3){int b=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=b;}
            mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        GameObject Object(Mesh mesh,bool collision=true)
        {
            var go=new GameObject("Geometry diagnostic fixture");objects.Add(go);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>();if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
        }
        static JObject Direction(JObject result,string axis)=>((JArray)result["meshColliders"][0]["directionalCoverage"]["byDirection"]).OfType<JObject>().Single(d=>(string)d["fromDirection"]==axis);

        [Test] public void FlippedSurfaceExposesBackfaceOnlyGeometryAndRealPhysicalMissWithoutRepair()
        {
            var mesh=Plane(true);var go=Object(mesh);var before=mesh.triangles.ToArray();var transform=go.transform.localToWorldMatrix;
            var result=Inspect(go.GetComponent<MeshFilter>());var above=Direction(result,"+Y");
            Assert.That((bool)result["analysisAvailable"],Is.True);Assert.That((int)result["worldFaceNormalDirections"]["-Y"],Is.EqualTo(2));
            Assert.That((int)above["twoSidedGeometryHits"],Is.EqualTo(9));Assert.That((int)above["frontFacingGeometryHits"],Is.Zero);Assert.That((int)above["colliderHits"],Is.Zero);
            CollectionAssert.AreEqual(before,mesh.triangles);Assert.That(go.transform.localToWorldMatrix,Is.EqualTo(transform));Assert.That(Physics.queriesHitBackfaces,Is.False);Assert.That((bool)result["readOnly"],Is.True);
        }
        [Test] public void CorrectSurfaceReportsFrontFacingAndActualCoverageFromAbove()
        {
            var go=Object(Plane());var result=Inspect(go.GetComponent<MeshCollider>());var above=Direction(result,"+Y");
            Assert.That((int)above["twoSidedGeometryHits"],Is.EqualTo(9));Assert.That((int)above["frontFacingGeometryHits"],Is.EqualTo(9));Assert.That((int)above["colliderHits"],Is.EqualTo(9));
            Assert.That((int)Direction(result,"-Y")["frontFacingGeometryHits"],Is.Zero);Assert.That((int)result["sampledTriangles"],Is.EqualTo(2));
        }
        [Test] public void GeometryOrientationIsMeasuredAfterMirroredTransform()
        {
            var go=Object(Plane());go.transform.localScale=new Vector3(-1,1,1);var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((float)result["transformDeterminant"],Is.LessThan(0));Assert.That((int)result["worldFaceNormalDirections"]["-Y"],Is.EqualTo(2));Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("mirrors")),Is.True);
        }
        [Test] public void DegenerateTrianglesAreCountedInsteadOfClaimingCollisionCoverage()
        {
            var mesh=Plane();mesh.triangles=new[]{0,0,1,0,1,2};var go=Object(mesh,false);var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((int)result["degenerateTrianglesInSample"],Is.EqualTo(1));Assert.That((int)result["sampledTriangles"],Is.EqualTo(2));Assert.That(((JArray)result["meshColliders"]).Count,Is.Zero);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("No MeshCollider")),Is.True);
        }
        [Test] public void DifferentCollisionMeshIsExplicitlyIdentified()
        {
            var go=Object(Plane());go.GetComponent<MeshCollider>().sharedMesh=Plane(true);var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["meshColliders"][0]["matchesInspectedMesh"],Is.False);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("different mesh")),Is.True);
        }
        [Test] public void UnreadableMeshProducesBoundedFactsWithoutAttemptingVertexRead()
        {
            var mesh=Plane();var go=Object(mesh,false);mesh.UploadMeshData(true);var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["analysisAvailable"],Is.False);Assert.That((int)result["mesh"]["vertices"],Is.EqualTo(4));Assert.That(result["triangleSamples"],Is.Null);
        }
        [Test] public void OversizedMeshDoesNotAllocateUnboundedTriangleAnalysis()
        {
            var mesh=new Mesh{name="Large diagnostic input",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};meshes.Add(mesh);mesh.vertices=new Vector3[65537];var go=Object(mesh,false);var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["analysisAvailable"],Is.False);Assert.That(result["warnings"].Values<string>().Any(s=>s.Contains("bounded inspection size")),Is.True);
        }
        [Test] public void GlobalBackfaceQueryFlagIsObservedAndNeverChangedByInspection()
        {
            Physics.queriesHitBackfaces=true;var go=Object(Plane(true));var result=Inspect(go.GetComponent<MeshFilter>());
            Assert.That((bool)result["queriesHitBackfaces"],Is.True);Assert.That(Physics.queriesHitBackfaces,Is.True);
        }
        [Test] public void ComponentInspectionExposesDiagnosticsWhileOrdinaryComponentsStaySmall()
        {
            var go=Object(Plane());var mesh=InspectComponent(go.GetComponent<MeshFilter>());var transform=InspectComponent(go.transform);
            Assert.That(mesh["geometryDiagnostics"]["analysisAvailable"].Value<bool>(),Is.True);Assert.That(transform["geometryDiagnostics"],Is.Null);Assert.That(mesh["fields"],Is.Not.Null);
        }
        [TestCase("plain")]
        [TestCase("light")]
        [TestCase("npc-style")]
        public void NonMeshObjectsHaveNoGeometrySummaryAndNeverDereferenceMissingComponents(string kind)
        {
            var go=new GameObject("Non-mesh "+kind);objects.Add(go);
            if(kind=="light")go.AddComponent<Light>();
            if(kind=="npc-style"){go.AddComponent<CharacterController>();go.AddComponent<Inventory>();}
            Assert.DoesNotThrow(()=>Assert.That(Snapshot(go),Is.Null));
        }
        [Test] public void SkinnedMeshWithoutMeshFilterIsStillReported()
        {
            var go=new GameObject("Skin without filter");objects.Add(go);go.AddComponent<SkinnedMeshRenderer>().sharedMesh=Plane();
            var summary=Snapshot(go);Assert.That((int)summary["visualMesh"]["vertices"],Is.EqualTo(4));
        }
        [TestCase(false,"outward")]
        [TestCase(true,"inward")]
        public void CompactSnapshotMeasuresClosedCoherentShellFacingWithoutRepair(bool inward,string expected)
        {
            var mesh=Tetrahedron(inward);var before=mesh.triangles.ToArray();var go=Object(mesh,false);var pose=go.transform.localToWorldMatrix;
            var orientation=Snapshot(go)["orientation"];
            Assert.That((string)orientation["classification"],Is.EqualTo("closed_coherent_shell"));Assert.That((bool)orientation["closedByIndices"],Is.True);Assert.That((bool)orientation["coherentlyWound"],Is.True);
            Assert.That((string)orientation["localGeometricFacing"],Is.EqualTo(expected));Assert.That((string)orientation["worldGeometricFacing"],Is.EqualTo(expected));Assert.That((double)orientation["signedVolumeLocal"],Is.EqualTo((inward?-1d:1d)/6).Within(1e-6));
            CollectionAssert.AreEqual(before,mesh.triangles);Assert.That(go.transform.localToWorldMatrix,Is.EqualTo(pose));Assert.That((bool)orientation["facingIsIntentDependent"],Is.True);
            if(inward)StringAssert.Contains("farther front face",(string)orientation["warning"]);
        }
        [Test] public void OpenSurfaceFacingRemainsAmbiguousRegardlessOfVolumeSign()
        {
            var go=Object(Plane(),false);var orientation=Snapshot(go)["orientation"];
            Assert.That((string)orientation["classification"],Is.EqualTo("ambiguous"));Assert.That((string)orientation["localGeometricFacing"],Is.EqualTo("ambiguous"));Assert.That((bool)orientation["closedByIndices"],Is.False);Assert.That((int)orientation["boundaryEdges"],Is.EqualTo(4));
        }
        [Test] public void MirroredShellSeparatesLocalAndWorldGeometricFacing()
        {
            var go=Object(Tetrahedron(),false);go.transform.localScale=new Vector3(-1,1,1);var orientation=Snapshot(go)["orientation"];
            Assert.That((string)orientation["localGeometricFacing"],Is.EqualTo("outward"));Assert.That((string)orientation["worldGeometricFacing"],Is.EqualTo("inward"));Assert.That((bool)orientation["mirroredTransform"],Is.True);Assert.That(go.transform.localScale,Is.EqualTo(new Vector3(-1,1,1)));
        }
        [Test] public void InconsistentClosedIndicesCannotBeClassifiedFromAggregateVolume()
        {
            var mesh=Tetrahedron();var triangles=mesh.triangles;int second=triangles[1];triangles[1]=triangles[2];triangles[2]=second;mesh.triangles=triangles;var go=Object(mesh,false);var orientation=Snapshot(go)["orientation"];
            Assert.That((bool)orientation["closedByIndices"],Is.True);Assert.That((bool)orientation["coherentlyWound"],Is.False);Assert.That((int)orientation["inconsistentEdges"],Is.EqualTo(3));Assert.That((string)orientation["localGeometricFacing"],Is.EqualTo("ambiguous"));
        }
        [Test] public void ReviewerGetsMeasuredCoverageWithoutOptionalInspectorCalls()
        {
            var go=Object(Tetrahedron(true));var before=go.GetComponent<MeshFilter>().sharedMesh.triangles.ToArray();var evidence=Review(new[]{go});
            Assert.That(evidence.Count,Is.EqualTo(1));Assert.That((string)evidence[0]["summary"]["orientation"]["localGeometricFacing"],Is.EqualTo("inward"));Assert.That(evidence[0]["measuredDiagnostics"]["triangleSamples"],Is.Null);
            Assert.That((int)evidence[0]["measuredDiagnostics"]["meshColliders"][0]["directionalCoverage"]["rays"],Is.EqualTo(54));StringAssert.Contains("far-side hit",(string)evidence[0]["physicalCoverageNote"]);CollectionAssert.AreEqual(before,go.GetComponent<MeshFilter>().sharedMesh.triangles);
        }
        [Test] public void ReviewerEvidenceHasBothObjectAndTextBudgets()
        {
            for(int i=0;i<8;i++){var go=Object(Plane());go.transform.position=Vector3.right*i*4;}
            var evidence=Review(objects);Assert.That(evidence.Count,Is.InRange(1,4));Assert.That(evidence.ToString(Newtonsoft.Json.Formatting.None).Length,Is.LessThanOrEqualTo(18000));
        }
        [Test] public void CompactSnapshotNeverReadsUploadedUnreadableMeshData()
        {
            var mesh=Tetrahedron();var go=Object(mesh,false);mesh.UploadMeshData(true);var summary=Snapshot(go);Assert.That((bool)summary["orientation"]["available"],Is.False);Assert.That(summary["orientation"]["signedVolumeLocal"],Is.Null);
        }
        [Test] public void CompactSnapshotKeepsDetailedRayArraysOutOfContext()
        {
            var go=Object(Plane());var summary=Snapshot(go);
            Assert.That((int)summary["visualMesh"]["vertices"],Is.EqualTo(4));Assert.That(summary["triangleSamples"],Is.Null);StringAssert.Contains("inspect_component",(string)summary["detailTool"]);
        }
        [Test] public void OppositeSharedTrianglesExposeCancelledStoredNormalsWithoutRepair()
        {
            var mesh=Plane();mesh.triangles=new[]{0,1,2,0,2,3,0,2,1,0,3,2};mesh.RecalculateNormals();
            var beforeNormals=mesh.normals.ToArray();var beforeIndices=mesh.triangles.ToArray();var go=Object(mesh,false);
            var normals=Snapshot(go)["storedVertexNormals"];
            Assert.IsTrue((bool)normals["available"]);Assert.AreEqual(4,(int)normals["count"]);Assert.IsTrue((bool)normals["matchesVertexCount"]);
            Assert.AreEqual(4,(int)normals["nearZeroCount"]);Assert.AreEqual(4,(int)normals["nonUnitFiniteCount"]);Assert.AreEqual(0,(int)normals["nonFiniteCount"]);
            Assert.AreEqual(0,(double)normals["minFiniteLength"],1e-7);Assert.AreEqual(0,(double)normals["maxFiniteLength"],1e-7);
            StringAssert.Contains("unlit/custom",(string)normals["warning"]);Assert.IsFalse((bool)normals["renderingDefectProven"]);
            Assert.AreEqual(4,(int)Inspect(go.GetComponent<MeshFilter>())["storedVertexNormals"]["nearZeroCount"]);
            CollectionAssert.AreEqual(beforeNormals,mesh.normals);CollectionAssert.AreEqual(beforeIndices,mesh.triangles);
        }
        [Test] public void ValidStoredNormalsAreMeasuredSeparatelyFromGeometricOrientation()
        {
            var mesh=Plane();var go=Object(mesh,false);var facts=Snapshot(go)["storedVertexNormals"];
            Assert.AreEqual(4,(int)facts["count"]);Assert.IsTrue((bool)facts["matchesVertexCount"]);Assert.AreEqual(0,(int)facts["nearZeroCount"]);
            Assert.AreEqual(0,(int)facts["nonFiniteCount"]);Assert.AreEqual(0,(int)facts["nonUnitFiniteCount"]);Assert.AreEqual(1,(double)facts["minFiniteLength"],1e-6);Assert.AreEqual(1,(double)facts["maxFiniteLength"],1e-6);Assert.IsNull(facts["warning"]);
        }
        [Test] public void MissingStoredNormalsRemainAnObservationNotAnAutomaticLightingRepair()
        {
            var mesh=Plane();mesh.normals=Array.Empty<Vector3>();var go=Object(mesh,false);var facts=Snapshot(go)["storedVertexNormals"];
            Assert.IsTrue((bool)facts["available"]);Assert.AreEqual(0,(int)facts["count"]);Assert.IsFalse((bool)facts["matchesVertexCount"]);
            Assert.AreEqual(JTokenType.Null,facts["minFiniteLength"].Type);Assert.AreEqual(JTokenType.Null,facts["maxFiniteLength"].Type);
            StringAssert.Contains("may be intentional",(string)facts["warning"]);Assert.IsFalse((bool)facts["renderingDefectProven"]);Assert.IsEmpty(mesh.normals);
        }
        [Test] public void StoredNormalStatisticsSeparateNonFiniteZeroAndFiniteNonUnitValues()
        {
            var implementation=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.GeometryDiagnostics",true);
            var values=new[]{Vector3.zero,Vector3.up*2,new Vector3(float.NaN,0,0),Vector3.up};
            var facts=(JObject)implementation.GetMethod("SummarizeStoredNormals",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{4,values});
            Assert.AreEqual(1,(int)facts["nonFiniteCount"]);Assert.AreEqual(1,(int)facts["nearZeroCount"]);Assert.AreEqual(2,(int)facts["nonUnitFiniteCount"]);
            Assert.AreEqual(0,(double)facts["minFiniteLength"]);Assert.AreEqual(2,(double)facts["maxFiniteLength"]);Assert.DoesNotThrow(()=>facts.ToString(Newtonsoft.Json.Formatting.None));Assert.IsTrue(float.IsNaN(values[2].x));
        }
        [Test] public void UnreadableAndOversizedMeshesDoNotReadStoredNormalArrays()
        {
            var unreadable=Plane();var go=Object(unreadable,false);unreadable.UploadMeshData(true);var facts=Snapshot(go)["storedVertexNormals"];
            Assert.IsFalse((bool)facts["available"]);Assert.IsNull(facts["count"]);StringAssert.Contains("not readable",(string)facts["reason"]);
            var large=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};meshes.Add(large);large.vertices=new Vector3[65537];go=Object(large,false);facts=Snapshot(go)["storedVertexNormals"];
            Assert.IsFalse((bool)facts["available"]);Assert.IsNull(facts["count"]);StringAssert.Contains("bounded",(string)facts["reason"]);
        }
        [Test] public void ReviewReceivesCancelledStoredNormalsAsNeutralMeasuredEvidence()
        {
            var mesh=Plane();mesh.normals=new Vector3[mesh.vertexCount];var go=Object(mesh,false);var result=Review(new[]{go});
            Assert.AreEqual(1,result.Count);Assert.AreEqual(4,(int)result[0]["summary"]["storedVertexNormals"]["nearZeroCount"]);
            Assert.AreEqual(4,(int)result[0]["measuredDiagnostics"]["storedVertexNormals"]["nearZeroCount"]);Assert.IsFalse((bool)result[0]["summary"]["storedVertexNormals"]["renderingDefectProven"]);
        }
    }
}
