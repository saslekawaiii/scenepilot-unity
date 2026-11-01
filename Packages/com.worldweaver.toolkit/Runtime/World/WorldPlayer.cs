using System;
using System.Linq;
using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Small playable exploration controller, usable without LLM or network access.</summary>
    [RequireComponent(typeof(CharacterController),typeof(Inventory))]
    public sealed class WorldPlayer : MonoBehaviour
    {
        public Camera View;
        public float Speed = 5, LookSpeed = 120, InteractDistance = 3;
        [Min(.1f)] public float Gravity = 9.81f;
        public bool UseLocalGravity;
        public bool MovementLocked;
        [Min(1)] public float SprintMultiplier = 1.6f;
        [Min(0)] public float JumpHeight = 1.1f;
        private CharacterController motor;
        private float yaw, vertical;
        private WorldConversationUI conversation;
        public WorldConversationUI Conversation => conversation;
        public string LastMessage = "WASD — движение · Shift — бег · Space — прыжок · правая кнопка мыши — поворот · E — разговор";
        public bool IsGrounded => motor && motor.isGrounded;
        public float PlanarSpeed => motor ? new Vector2(motor.velocity.x,motor.velocity.z).magnitude : 0;
        private void OnEnable()
        {
            motor = GetComponent<CharacterController>(); yaw = transform.eulerAngles.y; if (View) View.enabled = true;
            // Keep submillimetre walking/gravity steps at high frame rates; dropping them also resets isGrounded.
            motor.minMoveDistance = 0;
            if (Application.isPlaying)
            {
                conversation = GetComponentInChildren<WorldConversationUI>(true);
                if (!conversation) { var ui = new GameObject("WorldWeaver · интерфейс"); ui.transform.SetParent(transform, false); conversation = ui.AddComponent<WorldConversationUI>(); }
                conversation.Player = this; conversation.PlayerInventory = GetComponent<Inventory>();
            }
        }
        private void OnDisable() { if (View) View.enabled = false; if (conversation) conversation.Close(); }
        private void Update()
        {
            if (!motor || !motor.enabled) return;
            if (MovementLocked || (conversation && conversation.IsOpen)) return;
            var movement = WorldInput.Movement;
            yaw += WorldInput.TurnDegrees(LookSpeed,Time.deltaTime);
            transform.rotation = Quaternion.Euler(0,yaw,0);
            MoveFrame(movement,WorldInput.SprintHeld,WorldInput.JumpPressed,Time.deltaTime);
            if (WorldInput.InteractPressed)
            {var survival=GetComponent<WorldSurvival>();if(!survival||!survival.TryInteract())OpenNearestConversation();}
        }
        /// <summary>The same movement path is used by keyboard input and deterministic Play Mode checks.</summary>
        public void MoveFrame(Vector2 input, bool sprint, bool jump, float deltaTime)
        {
            if (MovementLocked || !motor || !motor.enabled || (conversation && conversation.IsOpen) || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            deltaTime = Mathf.Min(deltaTime,.1f);
            float gravity=UseLocalGravity?-Mathf.Max(.1f,Gravity):Physics.gravity.y;
            bool grounded = motor.isGrounded;
            if (grounded && vertical < 0) vertical = -2;
            if (grounded && jump && JumpHeight > 0) vertical = Mathf.Sqrt(2 * Mathf.Abs(gravity) * JumpHeight);
            vertical += gravity * deltaTime;
            var direction = Vector3.ClampMagnitude(new Vector3(input.x,0,input.y),1);
            float speed = Mathf.Max(0,Speed) * (sprint ? Mathf.Max(1,SprintMultiplier) : 1);
            motor.Move((transform.TransformDirection(direction)*speed + Vector3.up*vertical)*deltaTime);
        }
        public bool OpenNearestConversation()
        {
            var root = GetComponentInParent<WorldSaveState>();
            var npcs = (root ? root.GetComponentsInChildren<IntelligentNPC>() : transform.root.GetComponentsInChildren<IntelligentNPC>())
                .Where(n => n && n.isActiveAndEnabled).OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).ToArray();
            if (npcs.Length == 0 || Vector3.Distance(transform.position, npcs[0].transform.position) > InteractDistance)
            { LastMessage = "Подойдите ближе к персонажу, чтобы поговорить."; return false; }
            if (!conversation) return false;
            conversation.Open(npcs[0], GetComponent<Inventory>()); LastMessage = "Разговор: " + npcs[0].NpcName; return true;
        }
        public bool InteractNearest()
        {
            var root = GetComponentInParent<WorldSaveState>(); if (!root) return false;
            var quests = root.GetComponentsInChildren<DeliveryQuest>();
            var candidates = quests.SelectMany(q => new[] { q.Giver,q.Recipient }).Where(p => p).Distinct().OrderBy(p => Vector3.Distance(transform.position,p.transform.position)).ToArray();
            if (candidates.Length == 0 || Vector3.Distance(transform.position,candidates[0].transform.position) > InteractDistance) { LastMessage = "Рядом нет доступного персонажа. Найдите жителя на площади или стража."; return false; }
            var quest = quests.First(q => q.Giver == candidates[0] || q.Recipient == candidates[0]);
            bool result = quest.Interact(candidates[0],GetComponent<Inventory>()); LastMessage = quest.LastMessage; return result;
        }
    }
}
