using UnityEngine;

namespace WorldWeaver
{
    /// <summary>Drives imported authored locomotion clips from actual movement, never from generated text.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldCharacterAnimation : MonoBehaviour
    {
        public Animator Animator;
        public CharacterController Motor;
        public Transform Visual;
        public float TurnSpeed = 540;
        public float TeleportDistance = 4;
        public float MeasuredSpeed { get; private set; }
        private static readonly int Speed = UnityEngine.Animator.StringToHash("Speed");
        private static readonly int Grounded = UnityEngine.Animator.StringToHash("Grounded");
        private Vector3 previousPosition;
        private bool initialized;

        private void OnEnable()
        {
            if (!Motor) Motor = GetComponent<CharacterController>();
            if (!Animator) Animator = GetComponentInChildren<Animator>();
            previousPosition = transform.position;
            initialized = false;
            MeasuredSpeed = 0;
        }

        private void LateUpdate()
        {
            if (!Animator || !Animator.runtimeAnimatorController) return;
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            float dt = Time.deltaTime;
            Vector3 horizontal = new Vector3(delta.x, 0, delta.z);
            // A loaded save or a warp must not look like a sprint across the map.
            MeasuredSpeed = initialized && dt > .0001f && delta.sqrMagnitude < TeleportDistance * TeleportDistance
                ? Mathf.Min(horizontal.magnitude / dt, 20) : 0;
            initialized = true;
            Animator.SetFloat(Speed, MeasuredSpeed, .10f, dt);
            Animator.SetBool(Grounded, !Motor || Motor.isGrounded);
            if (Visual && MeasuredSpeed > .08f)
            {
                Quaternion facing = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                Visual.rotation = Quaternion.RotateTowards(Visual.rotation, facing, TurnSpeed * dt);
            }
        }
    }
}
