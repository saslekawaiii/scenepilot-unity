using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldWeaver
{
    /// <summary>Runtime conversation presentation. NPC services own inference, memory and validated actions.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldConversationUI : MonoBehaviour
    {
        public WorldPlayer Player;
        public Inventory PlayerInventory;
        public bool IsOpen => opened;
        public IntelligentNPC ActiveNpc { get; private set; }
        public VisualElement View => document ? document.rootVisualElement : null;
        public string LastError { get; private set; }

        UIDocument document;
        PanelSettings settings;
        VisualElement stage, profile, timeline, questCard;
        ScrollView history;
        TextField input;
        Label npcName, role, identity, status, worldTime, questText, location, notice, emptyHistory, hudNotice, inventoryText;
        Button send, cancel, questAction, aiSwitch;
        Image portrait;
        CancellationTokenSource request;
        Camera portraitCamera;
        RenderTexture portraitTexture;
        double nextHud, nextPortrait;
        string historySignature;
        int conversationVersion;
        bool sending, opened;

        void OnEnable() { EnsureView(); }
        void OnDisable()
        {
            Close();
            if (document) document.rootVisualElement.Clear();
            if (settings) Destroy(settings);
            settings = null;
        }

        public void EnsureView()
        {
            document = GetComponent<UIDocument>() ?? gameObject.AddComponent<UIDocument>();
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.name = "WorldWeaver conversation panel";
                // Preserve readable text in small Game views; geometry switches to
                // a single conversation column rather than shrinking indefinitely.
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.scale = Mathf.Clamp(Screen.height / 900f, .8f, 1.5f);
                settings.referenceResolution = new Vector2Int(1600, 900);
                settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                settings.match = .5f;
                settings.sortingOrder = 50;
                settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("WorldWeaver/UI/DialogueTheme");
            }
            document.panelSettings = settings;
            var root = document.rootVisualElement;
            root.Clear(); root.name = "worldweaver-game-ui"; root.AddToClassList("world-ui"); root.pickingMode = PickingMode.Ignore;
            var bodyFont = Resources.Load<Font>("WorldWeaver/UI/Fonts/NotoSans");
            if (bodyFont) root.style.unityFontDefinition = FontDefinition.FromFont(bodyFont);
            if (!GetComponent<NpcAmbientBubbles>()) gameObject.AddComponent<NpcAmbientBubbles>();
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("compact", e.newRect.width < 900));
            var hud = Box(root, "hud"); hud.pickingMode = PickingMode.Ignore;
            var brand = Box(hud, "world-brand");
            Label(brand, "WORLDWEAVER", "eyebrow");
            location = Label(brand, "ЛУННЫЙ БРОД", "location");
            worldTime = Label(hud, "", "world-time");
            questCard = Box(root, "quest-card");
            Label(questCard, "ПУТЕВОЙ ЖУРНАЛ", "eyebrow");
            questText = Label(questCard, "", "quest-text");
            inventoryText = Label(questCard, "", "inventory-text");
            var actions = Box(questCard, "hud-actions");
            Button(actions, "Сохранить", SaveGame, "hud-button");
            Button(actions, "Загрузить", LoadGame, "hud-button");
            Label(root, "W A S D   движение     ·     ПКМ   камера     ·     E   поговорить", "exploration-hint").pickingMode = PickingMode.Ignore;
            hudNotice = Label(root, "", "world-notice"); hudNotice.pickingMode = PickingMode.Ignore;

            var backdrop = Box(root, "dialogue-backdrop"); backdrop.name = "conversation-backdrop";
            stage = Box(backdrop, "dialogue-stage");
            profile = Box(stage, "character-card");
            Label(profile, "СОБЕСЕДНИК", "eyebrow");
            portrait = new Image { name = "character-portrait", scaleMode = ScaleMode.ScaleToFit }; portrait.AddToClassList("portrait"); profile.Add(portrait);
            identity = Label(profile, "", "character-name");
            role = Label(profile, "", "character-role");
            Label(profile, "Каждый разговор оставляет след.", "character-caption");

            var conversation = Box(stage, "conversation-panel");
            var header = Box(conversation, "conversation-header");
            var heading = Box(header, "conversation-heading");
            Label(heading, "РАЗГОВОР", "eyebrow");
            npcName = Label(heading, "", "conversation-title");
            var headingFont = Resources.Load<Font>("WorldWeaver/UI/Fonts/CormorantGaramond");
            if (headingFont) { identity.style.unityFontDefinition = FontDefinition.FromFont(headingFont); npcName.style.unityFontDefinition = FontDefinition.FromFont(headingFont); }
            aiSwitch = Button(header, "ИИ включён", ToggleAi, "ai-toggle");
            Button(header, "Закончить  ×", Close, "close-button");
            history = new ScrollView(ScrollViewMode.Vertical) { name = "conversation-history", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            history.AddToClassList("conversation-history"); conversation.Add(history);
            timeline = history.contentContainer;
            emptyHistory = Label(timeline, "", "conversation-intro");
            var contextActions = Box(conversation, "context-actions");
            questAction = Button(contextActions, "", PerformQuestAction, "quest-action");
            notice = Label(conversation, "", "conversation-notice"); notice.name = "conversation-notice";
            var compose = Box(conversation, "reply-composer");
            Label(compose, "ВАША РЕПЛИКА", "eyebrow");
            input = new TextField { name = "conversation-input", multiline = true, maxLength = 1600, isDelayed = false };
            input.AddToClassList("reply-input");
            input.tooltip = "Свободная реплика. Enter — отправить, Shift+Enter — новая строка.";
            compose.Add(input);
            input.RegisterValueChangedCallback(_ => RefreshControls());
            input.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !e.shiftKey && string.IsNullOrEmpty(Input.compositionString))
                { e.StopImmediatePropagation(); SendFromInput(); }
            }, TrickleDown.TrickleDown);
            var footer = Box(compose, "reply-footer");
            status = Label(footer, "Напишите свою реплику", "reply-status"); status.name = "conversation-status";
            cancel = Button(footer, "Отменить", CancelRequest, "cancel-button");
            send = Button(footer, "Отправить  ↑", SendFromInput, "send-button"); send.name = "conversation-send";
            Label(conversation, "ENTER   отправить       SHIFT + ENTER   новая строка       ESC   закончить разговор", "keyboard-hint");
            SetOpen(false);
            UpdateHud();
        }

        static VisualElement Box(VisualElement parent, string cls)
        { var element = new VisualElement(); element.AddToClassList(cls); parent.Add(element); return element; }
        static Label Label(VisualElement parent, string text, string cls)
        { var label = new Label(text) { enableRichText = false }; label.AddToClassList(cls); parent.Add(label); return label; }
        static Button Button(VisualElement parent, string text, Action action, string cls)
        { var button = new Button(action) { text = text }; button.AddToClassList(cls); parent.Add(button); return button; }

        void Update()
        {
            if (!document || View == null) return;
            settings.scale = Mathf.Clamp(Screen.height / 900f, .8f, 1.5f);
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
            if (Time.unscaledTimeAsDouble >= nextHud) { nextHud = Time.unscaledTimeAsDouble + .25; UpdateHud(); }
            if (IsOpen && Time.unscaledTimeAsDouble >= nextPortrait) { nextPortrait = Time.unscaledTimeAsDouble + .5; RefreshPortrait(); }
        }

        public void Open(IntelligentNPC npc, Inventory inventory = null)
        {
            if (!npc || !npc.isActiveAndEnabled) throw new ArgumentException("Выберите доступного персонажа.");
            Close();
            if (View == null || stage == null) EnsureView();
            PlayerInventory = inventory ? inventory : PlayerInventory;
            ActiveNpc = npc; conversationVersion++; LastError = null;
            npc.BeginPlayerConversation();
            npc.SpeechReceived += OnSpeech;
            npc.AiStatusChanged += OnStatus;
            identity.text = npc.NpcName; npcName.text = npc.NpcName; role.text = npc.Role;
            input.SetValueWithoutNotify(""); notice.text = "";
            historySignature = null; SetOpen(true); RefreshHistory(); RefreshControls(); RefreshQuestAction(); RefreshPortrait();
            StartCoroutine(FocusReplyAfterOpeningKey(conversationVersion));
        }

        IEnumerator FocusReplyAfterOpeningKey(int version)
        {
            // The E event that opened this view still reaches UI Toolkit later in
            // the frame. Focus only after release so it cannot become typed text.
            while (Input.GetKey(KeyCode.E)) yield return null;
            yield return null;
            if (IsOpen && version == conversationVersion) input.Focus();
        }

        public void Close()
        {
            conversationVersion++;
            CancelRequest();
            if (ActiveNpc)
            {
                ActiveNpc.SpeechReceived -= OnSpeech;
                ActiveNpc.AiStatusChanged -= OnStatus;
                ActiveNpc.EndPlayerConversation();
            }
            ActiveNpc = null; sending = false; historySignature = null;
            ReleasePortrait(); SetOpen(false);
        }

        void SetOpen(bool open)
        {
            opened = open;
            var backdrop = View?.Q("conversation-backdrop");
            if (backdrop != null) backdrop.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            View?.EnableInClassList("conversation-open", open);
        }
        void OnSpeech(NpcUtterance _) { if (isActiveAndEnabled) RefreshHistory(); }
        void OnStatus(string _) { if (isActiveAndEnabled) RefreshControls(); }

        void RefreshHistory()
        {
            if (!ActiveNpc || timeline == null) return;
            var entries = ActiveNpc.ConversationHistory;
            string signature = string.Join("|", entries.Select(e => e.Id));
            if (signature == historySignature) return;
            historySignature = signature; timeline.Clear();
            if (entries.Count == 0)
                emptyHistory = Label(timeline, "Вы подошли к персонажу. Поздоровайтесь, задайте вопрос или расскажите, что произошло — своими словами.", "conversation-intro");
            foreach (var entry in entries)
            {
                var row = Box(timeline, "utterance");
                row.AddToClassList(entry.IsPlayer ? "player-utterance" : "npc-utterance");
                Label(row, entry.IsPlayer ? "ВЫ" : entry.IsAi ? (entry.SpeakerName ?? ActiveNpc.NpcName).ToUpperInvariant() : "СИСТЕМА", "speaker-name");
                Label(row, entry.Text ?? "", "speech-text");
            }
            history.schedule.Execute(() => { if (history.panel != null) history.scrollOffset = new Vector2(0, history.verticalScroller.highValue); });
        }

        void RefreshControls()
        {
            if (!ActiveNpc || input == null) return;
            bool ready = !sending && !ActiveNpc.Busy && ActiveNpc.AiEnabled;
            input.SetEnabled(ready); send.SetEnabled(ready && !string.IsNullOrWhiteSpace(input.value));
            cancel.style.display = sending ? DisplayStyle.Flex : DisplayStyle.None;
            aiSwitch.text = ActiveNpc.AiEnabled ? "ИИ включён" : "Включить ИИ";
            aiSwitch.EnableInClassList("ai-disabled", !ActiveNpc.AiEnabled);
            status.text = sending ? "Обдумывает ответ…" : !ActiveNpc.AiEnabled ? "Свободный диалог выключен" : ActiveNpc.Busy ? "Заканчивает предыдущее действие…" : "Ваша реплика — своими словами";
        }

        async void SendFromInput()
        {
            if (sending || !ActiveNpc || ActiveNpc.Busy || !ActiveNpc.AiEnabled || string.IsNullOrWhiteSpace(input?.value)) return;
            try { await SendAsync(input.value); } catch (OperationCanceledException) { } catch (Exception) { /* Presented by SendAsync. */ }
        }

        public async Task<PipelineTrace> SendAsync(string text)
        {
            if (!ActiveNpc || sending || !ActiveNpc.AiEnabled) throw new InvalidOperationException("Разговор сейчас недоступен.");
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1600) throw new ArgumentException("Введите реплику длиной до 1600 символов.");
            var npc = ActiveNpc; int version = conversationVersion;
            var source = new CancellationTokenSource(); request = source; var token = source.Token;
            sending = true; LastError = null; notice.text = ""; input.SetValueWithoutNotify(""); RefreshControls();
            try
            {
                var trace = await npc.SpeakToPlayerAsync(text.Trim(), token);
                if (this && version == conversationVersion)
                {
                    if (trace.ReplySuppressed) notice.text = "Ответ отменён. Уже выполненные действия сохранены.";
                    else if (trace.Validation == null || !trace.Validation.Accepted)
                    {
                        LastError = "Модель не подготовила допустимый ответ. Ваша реплика сохранена — её можно уточнить или отправить снова.";
                        notice.text = LastError; input.SetValueWithoutNotify(text);
                    }
                    RefreshHistory(); RefreshQuestAction();
                }
                return trace;
            }
            catch (OperationCanceledException)
            {
                if (this && version == conversationVersion) { notice.text = "Ответ отменён. Можно продолжить разговор."; input.SetValueWithoutNotify(text); }
                throw;
            }
            catch (Exception)
            {
                if (this && version == conversationVersion)
                {
                    LastError = "Не удалось получить ответ. Проверьте, что выбранная модель запущена, и повторите реплику.";
                    notice.text = LastError; input.SetValueWithoutNotify(text);
                }
                throw;
            }
            finally
            {
                if (request == source) request = null;
                source.Dispose();
                if (this && version == conversationVersion) { sending = false; RefreshControls(); input.schedule.Execute(() => { if (IsOpen && !sending) input.Focus(); }); }
            }
        }

        public void CancelRequest() { request?.Cancel(); }
        void ToggleAi() { if (ActiveNpc) { ActiveNpc.AiEnabled = !ActiveNpc.AiEnabled; if (!ActiveNpc.AiEnabled) CancelRequest(); RefreshControls(); } }

        void RefreshQuestAction()
        {
            if (!ActiveNpc || questAction == null) return;
            var root = ActiveNpc.GetComponentInParent<WorldSaveState>();
            var place = ActiveNpc.GetComponent<WorldPlace>();
            var quest = root ? root.GetComponentsInChildren<DeliveryQuest>().FirstOrDefault(q => q.Giver == place || q.Recipient == place) : null;
            bool giver = quest && quest.Giver == place;
            bool can = quest && (giver && quest.Stage == DeliveryQuestStage.NotStarted || !giver && quest.Stage == DeliveryQuestStage.CarryingLetter);
            questAction.style.display = quest ? DisplayStyle.Flex : DisplayStyle.None;
            questAction.text = !quest ? "" : quest.Stage == DeliveryQuestStage.Completed ? "Письмо доставлено · награда получена" : giver ? quest.Stage == DeliveryQuestStage.NotStarted ? "По делу: взять письмо" : "Письмо уже у вас" : "По делу: передать письмо";
            questAction.SetEnabled(can && !sending && PlayerInventory && Vector3.Distance(PlayerInventory.transform.position, place.transform.position) <= quest.InteractionDistance);
        }
        void PerformQuestAction()
        {
            if (!ActiveNpc || !PlayerInventory || sending) return;
            var place = ActiveNpc.GetComponent<WorldPlace>(); var root = ActiveNpc.GetComponentInParent<WorldSaveState>();
            var quest = root ? root.GetComponentsInChildren<DeliveryQuest>().FirstOrDefault(q => q.Giver == place || q.Recipient == place) : null;
            if (!quest) return;
            quest.Interact(place, PlayerInventory); notice.text = quest.LastMessage ?? "";
            if (Player) Player.LastMessage = notice.text;
            RefreshQuestAction(); UpdateHud();
        }

        void UpdateHud()
        {
            if (IsOpen && (!ActiveNpc || !ActiveNpc.isActiveAndEnabled)) { Close(); return; }
            if (!IsOpen && ActiveNpc == null && sending) { Close(); return; }
            var save = Player ? Player.GetComponentInParent<WorldSaveState>() : PlayerInventory ? PlayerInventory.GetComponentInParent<WorldSaveState>() : GetComponentInParent<WorldSaveState>();
            var clock = save ? save.GetComponent<WorldClock>() : null;
            if (worldTime != null) worldTime.text = clock ? "День " + clock.Day + "   /   " + Mathf.FloorToInt(clock.Hour).ToString("00") + ":" + Mathf.FloorToInt(clock.Hour % 1 * 60).ToString("00") : "";
            if (location != null && save) location.text = save.gameObject.name.ToUpperInvariant();
            var quests = save ? save.GetComponentsInChildren<DeliveryQuest>() : Array.Empty<DeliveryQuest>();
            if (questText != null) questText.text = string.Join("\n\n", quests.Select(q => q.Title + "\n" + q.ObjectiveText));
            if (inventoryText != null && PlayerInventory) inventoryText.text = "В сумке: " + (PlayerInventory.Snapshot().Any(p => p.Value > 0) ? string.Join(", ", PlayerInventory.Snapshot().Where(p => p.Value > 0).Select(p => p.Key + " ×" + p.Value)) : "пусто");
            if (hudNotice != null) { hudNotice.text = Player ? Player.LastMessage : ""; hudNotice.style.display = !IsOpen && Player && !(Player.LastMessage ?? "").StartsWith("WASD", StringComparison.Ordinal) ? DisplayStyle.Flex : DisplayStyle.None; }
            if (questCard != null) questCard.style.display = save ? DisplayStyle.Flex : DisplayStyle.None;
            if (IsOpen) { RefreshControls(); RefreshQuestAction(); }
        }

        void SaveGame()
        {
            var save = Player ? Player.GetComponentInParent<WorldSaveState>() : PlayerInventory ? PlayerInventory.GetComponentInParent<WorldSaveState>() : GetComponentInParent<WorldSaveState>();
            if (!save) return;
            try { save.Save(); if (Player) Player.LastMessage = "Игра сохранена."; }
            catch (Exception) { if (Player) Player.LastMessage = "Сохранение недоступно во время действия персонажа."; }
        }
        void LoadGame()
        {
            var save = Player ? Player.GetComponentInParent<WorldSaveState>() : PlayerInventory ? PlayerInventory.GetComponentInParent<WorldSaveState>() : GetComponentInParent<WorldSaveState>();
            if (!save) return;
            Close();
            try { save.Load(); if (Player) Player.LastMessage = "Сохранение загружено."; }
            catch (Exception) { if (Player) Player.LastMessage = "Не удалось загрузить сохранение."; }
            UpdateHud();
        }

        void RefreshPortrait()
        {
            if (!ActiveNpc || portrait == null) return;
            if (ActiveNpc.Portrait) { portrait.image = ActiveNpc.Portrait; return; }
            var renderers = ActiveNpc.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) { portrait.image = null; return; }
            if (!portraitCamera)
            {
                var cameraObject = new GameObject("WorldWeaver portrait camera") { hideFlags = HideFlags.HideAndDontSave };
                portraitCamera = cameraObject.AddComponent<Camera>(); portraitCamera.enabled = false;
                portraitCamera.clearFlags = CameraClearFlags.SolidColor; portraitCamera.backgroundColor = new Color(.09f, .13f, .16f);
                portraitCamera.fieldOfView = 28; portraitCamera.nearClipPlane = .05f; portraitCamera.farClipPlane = 40;
                portraitTexture = new RenderTexture(320, 420, 24) { name = "WorldWeaver character portrait", hideFlags = HideFlags.HideAndDontSave };
                portraitCamera.targetTexture = portraitTexture; portrait.image = portraitTexture;
            }
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            var center = bounds.center + Vector3.up * Mathf.Min(.15f, bounds.extents.y * .1f);
            float distance = Mathf.Clamp(bounds.extents.magnitude * 3.1f, 2, 15);
            portraitCamera.transform.position = center + (ActiveNpc.transform.forward + Vector3.up * .08f).normalized * distance;
            portraitCamera.transform.LookAt(center); portraitCamera.Render();
        }
        void ReleasePortrait()
        {
            if (portrait != null) portrait.image = null;
            if (portraitCamera) Destroy(portraitCamera.gameObject);
            if (portraitTexture) { portraitTexture.Release(); Destroy(portraitTexture); }
            portraitCamera = null; portraitTexture = null;
        }
    }
}
