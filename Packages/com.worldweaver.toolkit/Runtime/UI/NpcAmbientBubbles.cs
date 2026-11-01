using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldWeaver
{
    /// <summary>Displays actual nearby NPC-to-NPC utterances, without generating or simulating speech.</summary>
    [RequireComponent(typeof(WorldConversationUI))]
    public sealed class NpcAmbientBubbles : MonoBehaviour
    {
        sealed class Bubble { public IntelligentNPC Actor; public VisualElement Element; public float Expires; }
        readonly Dictionary<IntelligentNPC, Action<NpcUtterance>> subscriptions = new Dictionary<IntelligentNPC, Action<NpcUtterance>>();
        readonly List<Bubble> bubbles = new List<Bubble>();
        WorldConversationUI host;
        float nextScan;
        void OnEnable() { host = GetComponent<WorldConversationUI>(); nextScan = 0; }
        void OnDisable()
        {
            foreach (var pair in subscriptions) if (pair.Key) pair.Key.SpeechReceived -= pair.Value;
            subscriptions.Clear(); foreach (var bubble in bubbles) bubble.Element.RemoveFromHierarchy(); bubbles.Clear();
        }
        void Update()
        {
            if (!host || host.View?.panel == null) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 2; UpdateSubscriptions(); }
            var camera = host.Player && host.Player.View ? host.Player.View : Camera.main;
            foreach (var bubble in bubbles.ToArray())
            {
                if (!bubble.Actor || Time.unscaledTime >= bubble.Expires) { bubble.Element.RemoveFromHierarchy(); bubbles.Remove(bubble); continue; }
                if (!camera || host.IsOpen || Vector3.Distance(camera.transform.position, bubble.Actor.transform.position) > 45)
                { bubble.Element.style.display = DisplayStyle.None; continue; }
                var screen = camera.WorldToScreenPoint(bubble.Actor.transform.position + Vector3.up * 2.3f);
                bool visible = screen.z > 0 && screen.x > 0 && screen.x < Screen.width && screen.y > 0 && screen.y < Screen.height;
                bubble.Element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                var position = RuntimePanelUtils.ScreenToPanel(host.View.panel, new Vector2(screen.x, Screen.height - screen.y));
                bubble.Element.style.left = position.x; bubble.Element.style.top = position.y;
                bubble.Element.style.opacity = Mathf.Clamp01((bubble.Expires - Time.unscaledTime) / .6f);
            }
        }
        void UpdateSubscriptions()
        {
            var owner = host.Player ? host.Player.transform : host.ActiveNpc ? host.ActiveNpc.transform : transform;
            var save = owner.GetComponentInParent<WorldSaveState>();
            var actors = (save ? save.GetComponentsInChildren<IntelligentNPC>() : owner.root.GetComponentsInChildren<IntelligentNPC>()).Where(n => n && n.isActiveAndEnabled).ToArray();
            foreach (var actor in subscriptions.Keys.ToArray())
                if (!actor || !actors.Contains(actor)) { if (actor) actor.SpeechReceived -= subscriptions[actor]; subscriptions.Remove(actor); }
            foreach (var actor in actors)
            {
                if (subscriptions.ContainsKey(actor)) continue;
                Action<NpcUtterance> handler = line => Show(actor, line); subscriptions[actor] = handler; actor.SpeechReceived += handler;
            }
        }
        void Show(IntelligentNPC actor, NpcUtterance line)
        {
            if (!host || host.View == null || !actor || line == null || line.IsPlayer || line.SpeakerId != actor.CharacterId || !line.IsAi || string.IsNullOrWhiteSpace(line.TargetId) || string.Equals(line.TargetId, "Player", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var old in bubbles.Where(b => b.Actor == actor).ToArray()) { old.Element.RemoveFromHierarchy(); bubbles.Remove(old); }
            if (bubbles.Count >= 6) { bubbles[0].Element.RemoveFromHierarchy(); bubbles.RemoveAt(0); }
            var element = new VisualElement { pickingMode = PickingMode.Ignore }; element.AddToClassList("ambient-speech");
            var name = new Label(actor.NpcName) { enableRichText = false, pickingMode = PickingMode.Ignore }; name.AddToClassList("ambient-speaker"); element.Add(name);
            var text = line.Text ?? "";
            var speech = new Label(text.Length > 240 ? text.Substring(0, 237) + "…" : text) { enableRichText = false, pickingMode = PickingMode.Ignore }; speech.AddToClassList("ambient-text"); element.Add(speech);
            host.View.Add(element); bubbles.Add(new Bubble { Actor = actor, Element = element, Expires = Time.unscaledTime + Mathf.Clamp(text.Length / 18f, 5, 14) });
        }
    }
}
