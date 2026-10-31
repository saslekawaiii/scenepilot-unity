using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    public enum SceneBehaviorKind { Rotate, Bob, Patrol, FollowPlayer }
    /// <summary>Allowlisted behaviours. Movement requires actual complete Unity NavMesh paths.</summary>
    public sealed class SceneBehavior : MonoBehaviour
    {
        public SceneBehaviorKind Mode;
        public float Speed = 2, Amplitude = .4f, Frequency = 1;
        public Vector3 Axis = Vector3.up;
        public Vector3[] Waypoints = new Vector3[0];
        public WorldPlace[] NamedWaypoints = new WorldPlace[0];
        public Transform FollowTarget;
        public float StoppingDistance = 2;
        public string LastNavigationError { get; private set; }
        public int CompletedLegs { get; private set; }
        private Vector3 origin;
        private float elapsed, nextPath;
        private int waypoint;
        private NavMeshAgent agent;
        private void OnEnable() { origin = transform.position; elapsed = nextPath = 0; waypoint = CompletedLegs = 0; }
        private void OnDisable() { if (agent && agent.enabled && agent.isOnNavMesh) agent.ResetPath(); }
        private void Update()
        {
            float dt = Time.deltaTime; elapsed += dt;
            if (Mode == SceneBehaviorKind.Rotate) { transform.Rotate(Axis.normalized, Speed * dt, Space.Self); return; }
            if (Mode == SceneBehaviorKind.Bob) { transform.position = origin + Axis.normalized * (Mathf.Sin(elapsed * Frequency * Mathf.PI * 2) * Amplitude); return; }
            if (Mode == SceneBehaviorKind.FollowPlayer && !FollowTarget) return;
            int count = NamedWaypoints != null && NamedWaypoints.Length > 0 ? NamedWaypoints.Length : Waypoints?.Length ?? 0;
            if (Mode == SceneBehaviorKind.Patrol && count == 0) return;
            waypoint = count > 0 ? waypoint % count : 0;
            if (Mode == SceneBehaviorKind.Patrol && NamedWaypoints != null && NamedWaypoints.Length > 0 && !NamedWaypoints[waypoint]) { LastNavigationError = "Named place was deleted."; return; }
            Vector3 destination = Mode == SceneBehaviorKind.FollowPlayer ? FollowTarget.position : NamedWaypoints != null && NamedWaypoints.Length > 0 ? NamedWaypoints[waypoint].ArrivalPosition : Waypoints[waypoint];
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!agent) { LastNavigationError = "Add a NavMeshAgent and bake navigation first."; return; }
            if (!agent.enabled || !agent.isOnNavMesh) { LastNavigationError = "NPC is not on a NavMesh."; return; }
            agent.speed = Mathf.Clamp(Speed,.01f,20); agent.stoppingDistance = Mode == SceneBehaviorKind.Patrol ? .15f : StoppingDistance;
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .5f;
                if (!WorldNavigation.TryPath(transform.position,destination,out var path)) { agent.ResetPath(); LastNavigationError = "Destination is unreachable."; return; }
                agent.isStopped = false; agent.SetPath(path); LastNavigationError = null;
            }
            if (Mode == SceneBehaviorKind.Patrol && !agent.pathPending && agent.hasPath && agent.remainingDistance <= agent.stoppingDistance + .1f) { waypoint = (waypoint + 1) % count; CompletedLegs++; nextPath = 0; }
        }
    }
}
