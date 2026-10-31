using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace WorldWeaver
{
    /// <summary>A generated world's real NPC dialogue preview. Reads actual inventories and pipeline results.</summary>
    public sealed class WorldPlayground : MonoBehaviour
    {
        private const int MaxInputLength = 3000;
        private readonly List<string> history = new List<string>();
        private IntelligentNPC[] characters = Array.Empty<IntelligentNPC>();
        private int selected;
        private string input = "Расскажи о себе и о том, что ты знаешь об этом мире.";
        private string status = "Выберите персонажа и напишите сообщение.";
        private bool busy;
        private CancellationTokenSource request;
        private Vector2 scroll;
        private GUIStyle title, body, bubble, small, send;
        private Texture2D panelTexture, bubbleTexture;
        private WorldEntity visitor;

        private void Start()
        {
            characters = FindObjectsByType<IntelligentNPC>(FindObjectsSortMode.None).Where(n => n.gameObject.scene == gameObject.scene && n.GetComponent<GeneratedSceneObject>()).ToArray();
            visitor = FindObjectsByType<WorldEntity>(FindObjectsSortMode.None).FirstOrDefault(e => e.Id == "Player" && e.gameObject.scene == gameObject.scene);
            if (!visitor && characters.Length > 0)
            {
                var go = new GameObject("Chat Visitor"); go.transform.SetParent(transform); visitor = go.AddComponent<WorldEntity>(); visitor.Id = "Player";
                go.AddComponent<Inventory>().Add("Coin", 20);
            }
            foreach (var npc in characters)
            {
                if (!visitor) continue;
                // Keep the actual visitor first in the bounded model context. Duplicate
                // Player IDs would also make the action dispatcher reject every decision.
                npc.KnownEntities.RemoveAll(entity => entity == visitor || entity && entity.Id == "Player");
                npc.KnownEntities.Insert(0, visitor);
            }
            PlaceVisitor();
        }
        private void PlaceVisitor() { if (visitor && characters.Length > 0) visitor.transform.position = characters[selected].transform.position + Vector3.right * 2; }
        private static Texture2D Texture(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
        private void Styles()
        {
            if (title != null) return;
            panelTexture = Texture(new Color(.075f, .085f, .10f, .96f)); bubbleTexture = Texture(new Color(.14f, .16f, .19f));
            title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = false, normal = { textColor = new Color(.9f, .92f, .95f) } };
            small = new GUIStyle(body) { fontSize = 11, normal = { textColor = new Color(.63f, .68f, .74f) } };
            bubble = new GUIStyle(body) { padding = new RectOffset(13, 13, 10, 10), margin = new RectOffset(0, 0, 4, 8), normal = { textColor = Color.white, background = bubbleTexture } };
            send = new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 34 };
        }
        private void OnGUI()
        {
            if (characters.Length == 0) return;
            Styles(); float width = Mathf.Min(440, Screen.width * .48f);
            var panel = new Rect(Screen.width - width - 16, 16, width, Screen.height - 32);
            GUI.DrawTexture(panel, panelTexture);
            GUILayout.BeginArea(new Rect(panel.x + 20, panel.y + 18, width - 40, panel.height - 36));
            GUILayout.Label("Диалог с персонажем", title);
            GUILayout.Label("Индивидуальные знания · настоящее состояние мира", small); GUILayout.Space(12);
            using (new GUIEnabled(!busy))
            {
                int next = GUILayout.SelectionGrid(selected, characters.Select(n => n.NpcName).ToArray(), Math.Min(3, characters.Length), GUILayout.Height(32));
                if (next != selected) { selected = next; history.Clear(); PlaceVisitor(); }
            }
            var npc = characters[selected];
            GUILayout.Label(npc.Role + " · " + npc.Personality, small); GUILayout.Space(9);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            if (history.Count == 0) GUILayout.Label("Напишите обычный вопрос. Персонаж отвечает через выбранную модель и свою базу знаний.", body);
            foreach (var line in history) GUILayout.Label(line, bubble);
            GUILayout.EndScrollView();
            GUILayout.Label(status, small);
            if (visitor && visitor.GetComponent<Inventory>()) GUILayout.Label("Ваш инвентарь: " + string.Join(", ", visitor.GetComponent<Inventory>().Snapshot().Select(p => p.Key + " × " + p.Value)), small);
            GUILayout.Space(8);
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !Event.current.shift && GUI.GetNameOfFocusedControl() == "worldweaver-npc-input";
            if (enter) Event.current.Use();
            GUI.SetNextControlName("worldweaver-npc-input"); input = GUILayout.TextArea(input, MaxInputLength, GUILayout.Height(66));
            using (new GUIEnabled(!busy && !string.IsNullOrWhiteSpace(input))) if (GUILayout.Button("Отправить", send) || enter) Ask();
            if (busy && GUILayout.Button("Остановить", send)) request?.Cancel();
            GUILayout.EndArea();
        }
        private async void Ask()
        {
            if (busy || characters.Length == 0) return;
            string question = (input ?? "").Trim();
            if (question.Length == 0) { status = "Введите сообщение персонажу."; return; }
            if (question.Length > MaxInputLength) { status = "Сообщение слишком длинное: максимум 3000 символов."; return; }
            var npc = characters[selected]; input = "";
            history.Add("Вы\n" + question); busy = true; status = "Персонаж думает и ищет свои знания…"; request = new CancellationTokenSource();
            try
            {
                var trace = await npc.TalkAsync(question, request.Token);
                history.Add(npc.NpcName + "\n" + (string.IsNullOrWhiteSpace(trace.Speech) ? "Ответ не доставлен: " + trace.Validation?.Reason : trace.Speech));
                status = trace.Validation?.Accepted == true ? "Ответ получен · " + trace.TotalMs + " мс" : "Предложенное действие не прошло проверку.";
            }
            catch (OperationCanceledException) { status = "Запрос остановлен."; }
            catch (Exception error) { status = error is ProviderException ? "Модель недоступна. Проверьте Ollama или облачный сервис." : "Не удалось ответить: " + error.GetType().Name; }
            finally { busy = false; request?.Dispose(); request = null; if (history.Count > 40) history.RemoveRange(0, history.Count - 40); scroll.y = float.MaxValue; }
        }
        private void OnDisable() { request?.Cancel(); }
        private void OnDestroy() { if (panelTexture) Destroy(panelTexture); if (bubbleTexture) Destroy(bubbleTexture); }
        private sealed class GUIEnabled : IDisposable
        {
            private readonly bool before;
            public GUIEnabled(bool enabled) { before = GUI.enabled; GUI.enabled = before && enabled; }
            public void Dispose() { GUI.enabled = before; }
        }
    }
}
