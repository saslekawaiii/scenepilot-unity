using UnityEngine;
namespace WorldWeaver
{
    public enum SceneBehaviorKind { Rotate, Bob, Patrol, FollowPlayer }
    /// <summary>Small allowlisted behaviours. Never evaluates code or looks up component types.</summary>
    public sealed class SceneBehavior : MonoBehaviour
    {
        public SceneBehaviorKind Mode;
        public float Speed = 2;
        public float Amplitude = .4f;
        public float Frequency = 1;
        public Vector3 Axis = Vector3.up;
        public Vector3[] Waypoints = new Vector3[0];
        public Transform FollowTarget;
        public float StoppingDistance = 2;
        private Vector3 origin;
        private float elapsed;
        private int waypoint;
        private void OnEnable() { origin = transform.position; elapsed = 0; waypoint = 0; }
        private void Update()
        {
            float dt = Time.deltaTime; elapsed += dt;
            switch (Mode)
            {
                case SceneBehaviorKind.Rotate: transform.Rotate(Axis.normalized, Speed * dt, Space.Self); break;
                case SceneBehaviorKind.Bob: transform.position = origin + Axis.normalized * (Mathf.Sin(elapsed * Frequency * Mathf.PI * 2) * Amplitude); break;
                case SceneBehaviorKind.Patrol:
                    if (Waypoints == null || Waypoints.Length == 0) return;
                    waypoint %= Waypoints.Length;
                    var destination = Waypoints[waypoint];
                    FaceAndMove(destination, dt, 0);
                    if (Vector3.Distance(transform.position, destination) < .05f) waypoint = (waypoint + 1) % Waypoints.Length;
                    break;
                case SceneBehaviorKind.FollowPlayer:
                    if (FollowTarget) FaceAndMove(FollowTarget.position, dt, StoppingDistance);
                    break;
            }
        }
        private void FaceAndMove(Vector3 destination, float dt, float stop)
        {
            var delta = destination - transform.position;
            if (delta.magnitude <= stop) return;
            transform.position = Vector3.MoveTowards(transform.position, destination, Mathf.Min(Speed * dt, delta.magnitude - stop));
            delta.y = 0;
            if (delta.sqrMagnitude > .001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(delta), dt * 6);
        }
    }
}
