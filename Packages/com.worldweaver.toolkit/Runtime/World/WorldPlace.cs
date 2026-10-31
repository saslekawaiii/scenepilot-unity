using System;
using UnityEngine;
namespace WorldWeaver
{
    public enum WorldPlaceKind { Square, House, Entrance, Gate, Road, Spawn, Npc, Quest, Waypoint }
    /// <summary>Persistent identity and a semantic destination within one world.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldPlace : MonoBehaviour
    {
        public string PersistentId = Guid.NewGuid().ToString("N");
        public string Key;
        public WorldPlaceKind Kind;
        public WorldPlace[] Connections = Array.Empty<WorldPlace>();
        public Vector3 ArrivalOffset;
        public Vector3 ArrivalPosition => transform.TransformPoint(ArrivalOffset);
        public bool IsConnectedTo(WorldPlace other) => Array.IndexOf(Connections, other) >= 0;
    }
}
