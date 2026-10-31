using System;
using System.Collections.Generic;
using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Explicit ownership and persistent identity; names never confer ownership.</summary>
    [DisallowMultipleComponent, ExecuteAlways]
    public sealed class GeneratedSceneObject : MonoBehaviour
    {
        [HideInInspector] public string ObjectId = Guid.NewGuid().ToString("N");
        public string Kind;
        public string BatchId;
        public bool SurfaceAnchored;
        public string Style;
        public static bool SuspendIdentityRegistration;
        private static readonly Dictionary<string,GeneratedSceneObject> identities = new Dictionary<string,GeneratedSceneObject>();
        private void OnEnable()
        {
            // A checkpoint is a temporary serialization copy, not a newly authored scene object.
            if(SuspendIdentityRegistration)return;
            if(gameObject.scene.path.StartsWith("Assets/__WorldWeaverCheckpoint-",StringComparison.Ordinal))return;
            EnsureUniqueIdentity();
        }
        public void EnsureUniqueIdentity()
        {
            if (!Guid.TryParseExact(ObjectId,"N",out _) || identities.TryGetValue(ObjectId,out var other) && other && other != this) ObjectId=Guid.NewGuid().ToString("N");
            identities[ObjectId]=this;
        }
        private void OnDestroy() { if(!string.IsNullOrEmpty(ObjectId)&&identities.TryGetValue(ObjectId,out var owner)&&owner==this)identities.Remove(ObjectId); }
    }
}
