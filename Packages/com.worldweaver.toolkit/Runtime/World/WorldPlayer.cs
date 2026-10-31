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
        public string LastMessage = "WASD — движение · правая кнопка мыши — камера · E — действие";
        private void OnEnable() { motor = GetComponent<CharacterController>(); yaw = transform.eulerAngles.y; if (View) View.enabled = true; }
        private void OnDisable() { if (View) View.enabled = false; }
        private void Update()
        {
            if (!motor || !motor.enabled) return;
            float x = Input.GetAxisRaw("Horizontal"), z = Input.GetAxisRaw("Vertical");
            if (Input.GetMouseButton(1)) yaw += Input.GetAxis("Mouse X") * LookSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0,yaw,0);
            vertical = motor.isGrounded && vertical < 0 ? -2 : vertical + Physics.gravity.y * Time.deltaTime;
            motor.Move((transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x,0,z),1)) * Speed + Vector3.up * vertical) * Time.deltaTime);
            if (Input.GetKeyDown(KeyCode.E)) InteractNearest();
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
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16,16,Mathf.Min(490,Screen.width-32),220),GUI.skin.box);
            GUILayout.Label(LastMessage);
            var save = GetComponentInParent<WorldSaveState>();
            if (save)
            {
                var clock=save.GetComponent<WorldClock>();if(clock) { GUILayout.Label("День "+clock.Day+" · "+Mathf.FloorToInt(clock.Hour).ToString("00")+":"+Mathf.FloorToInt(clock.Hour%1*60).ToString("00"));GUILayout.BeginHorizontal();if(GUILayout.Button("Утро (08:00)"))clock.SetTime(8);if(GUILayout.Button("Ночь (22:00)"))clock.SetTime(22);GUILayout.EndHorizontal(); }
                foreach (var quest in save.GetComponentsInChildren<DeliveryQuest>()) GUILayout.Label(quest.Title + ": " + (quest.Stage == DeliveryQuestStage.Completed ? "выполнено" : quest.Stage == DeliveryQuestStage.CarryingLetter ? "доставьте письмо стражу" : "получите письмо у жителя"));
                GUILayout.BeginHorizontal(); if (GUILayout.Button("Действие (E)")) InteractNearest();
                if (GUILayout.Button("Сохранить")) { try { save.Save(); LastMessage = "Игра сохранена."; } catch (Exception e) { LastMessage = e.Message; } }
                if (GUILayout.Button("Загрузить")) { try { save.Load(); LastMessage = "Сохранение загружено."; } catch (Exception e) { LastMessage = e.Message; } }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Инвентарь: " + string.Join(", ",GetComponent<Inventory>().Snapshot().Where(p => p.Value > 0).Select(p => p.Key + " ×" + p.Value)));
            GUILayout.EndArea();
        }
    }
}
