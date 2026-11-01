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
        private CharacterController motor;
        private float yaw, vertical;
        private WorldConversationUI conversation;
        public WorldConversationUI Conversation => conversation;
        public string LastMessage = "WASD — движение · правая кнопка мыши — камера · E — действие";
        private void OnEnable()
        {
            motor = GetComponent<CharacterController>(); yaw = transform.eulerAngles.y; if (View) View.enabled = true;
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
            if (conversation && conversation.IsOpen) return;
            float x = Input.GetAxisRaw("Horizontal"), z = Input.GetAxisRaw("Vertical");
            if (Input.GetMouseButton(1)) yaw += Input.GetAxis("Mouse X") * LookSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0,yaw,0);
            vertical = motor.isGrounded && vertical < 0 ? -2 : vertical + Physics.gravity.y * Time.deltaTime;
            motor.Move((transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x,0,z),1)) * Speed + Vector3.up * vertical) * Time.deltaTime);
            if (Input.GetKeyDown(KeyCode.E)) OpenNearestConversation();
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
