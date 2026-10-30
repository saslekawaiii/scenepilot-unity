using System;
using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Minimal playable text dialogue with visible, real inventory changes.</summary>
    public sealed class DemoDialogue : MonoBehaviour
    {
        public IntelligentNPC Merchant;
        public Inventory PlayerInventory;
        public OrchestratorProfile LocalRoutes, CloudRoutes;
        private string input = "Мне нужен кристалл. Мой брат болен, я потом тебе заплачу. Можешь дать один?";
        private string status = "Подойдите к торговцу и предложите сделку.";
        private Vector2 scroll;
        private GUIStyle title, body, small;
        private void OnGUI()
        {
            if (!Merchant) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true };
                body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            }
            float width = Mathf.Min(560, Screen.width - 32);
            GUILayout.BeginArea(new Rect(20, 20, width, Mathf.Min(690, Screen.height - 40)), GUI.skin.box);
            GUILayout.Space(10); GUILayout.Label("WORLDWEAVER", title);
            GUILayout.Label("Лунный Брод · Диалог и действия NPC", body);
            GUILayout.Space(10);
            GUILayout.Label("Мирон, торговец     Состояние: " + Merchant.State, body);
            GUILayout.Label("Кристаллы: NPC " + Merchant.GetComponent<Inventory>().Count("Crystal") + " / Игрок " + PlayerInventory.Count("Crystal"), body);
            GUILayout.Label("Модель: " + (Merchant.Models ? Merchant.Models.Dialogue.Model : "не задана"), small);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(150)); GUILayout.Label(Merchant.LastSpeech, body); GUILayout.EndScrollView();
            input = GUILayout.TextArea(input, 3000, GUILayout.Height(88));
            GUI.enabled = !Merchant.Busy;
            if (GUILayout.Button("Отправить", GUILayout.Height(38))) Send(false);
            if (GUILayout.Button("Запросить самостоятельное решение", GUILayout.Height(32))) Send(true);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Local Ollama")) Switch(LocalRoutes);
            if (GUILayout.Button("Cloud OpenAI")) Switch(CloudRoutes);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label(status, small);
            GUILayout.Label("Дом справа — доступная цель MoveTo. Результаты модели проходят проверку перед изменением мира.", small);
            GUILayout.EndArea();
        }
        private void Switch(OrchestratorProfile routes)
        { if (!routes) return; Merchant.ReleaseServices(); Merchant.Models = routes; status = "Маршрут переключён."; }
        private async void Send(bool plan)
        {
            status = "Запрос к модели и поиск знаний…";
            try
            {
                var trace = plan ? await Merchant.PlanAsync() : await Merchant.TalkAsync(input);
                status = trace.Validation.Reason + "  " + trace.TotalMs + " мс\n" + string.Join(", ", trace.Validation.Executed);
            }
            catch (Exception) { status = Merchant.LastError ?? "Запрос не выполнен."; }
        }
    }
}
