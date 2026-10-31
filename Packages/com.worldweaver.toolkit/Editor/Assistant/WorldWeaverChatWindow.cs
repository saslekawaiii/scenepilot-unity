using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Chat presentation only. The controller owns persistence, inference and all changes to the project.</summary>
    public sealed class WorldWeaverChatWindow : EditorWindow
    {
        const string StylePath = "Packages/com.worldweaver.toolkit/Editor/Assistant/Chat.uss";
        static readonly List<string> PowerLabels = new List<string> { "Быстро", "Баланс", "Глубоко" };
        [SerializeField] string draft = "";
        [SerializeField] string attachedSelection = "";
        [SerializeField] string attachmentTitle = "";
        [SerializeField] bool sidebarVisible = true;
        [SerializeField] string lastSessionId;

        ChatController controller;
        readonly Dictionary<string, MessageView> messageViews = new Dictionary<string, MessageView>();
        readonly Dictionary<string, Vector2> scrollPositions = new Dictionary<string, Vector2>();
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly List<AvailableModel> modelChoices = new List<AvailableModel>();
        VisualElement sidebar, messageList, welcome, taskMenu, notice, attachment;
        ScrollView chatScroll, historyScroll;
        TextField composer;
        Label phaseLabel, connectionLabel, titleLabel, noticeLabel, attachmentLabel, placeholder;
        Button sendButton, refreshButton, newChatButton, undoButton;
        DropdownField modelDropdown, powerDropdown;
        Toggle autoApply;
        string renderedSessionId, historySignature, modelsSignature;
        bool updatingControls, rendering, refreshingModels;
        bool lastEditorCompiling, lastEditorPlaying;
        int lifecycleVersion;
        int needsRefresh, scrollVersion;

        [MenuItem("Tools/WorldWeaver/Чат", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldWeaverChatWindow>();
            window.titleContent = new GUIContent("WorldWeaver · Чат");
            window.minSize = new Vector2(780, 600);
            window.Show();
            window.Focus();
        }

        void OnEnable()
        {
            EnsureController();
            EditorApplication.update += PumpChanges;
            ScriptDrafts.Changed += MarkChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= PumpChanges;
            ScriptDrafts.Changed -= MarkChanged;
            lifecycleVersion++;
            refreshingModels = false;
            if (controller == null) return;
            controller.Changed -= MarkChanged;
            controller.Dispose();
            controller = null;
        }

        void EnsureController()
        {
            if (controller != null) return;
            controller = new ChatController();
            if (!string.IsNullOrEmpty(lastSessionId)) controller.OpenChat(lastSessionId);
            controller.Changed += MarkChanged;
        }

        // Changed may originate from an async continuation. Only this flag crosses threads.
        void MarkChanged() => Interlocked.Exchange(ref needsRefresh, 1);

        void PumpChanges()
        {
            // Compiler callbacks can run before isCompiling returns to false. Refresh
            // action availability on that transition, including after a domain reload.
            bool compiling = EditorApplication.isCompiling;
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (compiling != lastEditorCompiling || playing != lastEditorPlaying)
            {
                lastEditorCompiling = compiling; lastEditorPlaying = playing;
                MarkChanged();
            }
            if (Interlocked.Exchange(ref needsRefresh, 0) != 0 && composer != null) Render();
        }

        public void CreateGUI()
        {
            EnsureController();
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("ww-chat");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            // Keep the Editor's theme sheets: they provide the default text font.
            // Clear only our content when rebuilding after a domain reload.
            if (style != null && !rootVisualElement.styleSheets.Contains(style))
                rootVisualElement.styleSheets.Add(style);
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnWindowKey, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnWindowKey, TrickleDown.TrickleDown);
            rootVisualElement.UnregisterCallback<PointerDownEvent>(OnRootPointerDown);
            rootVisualElement.RegisterCallback<PointerDownEvent>(OnRootPointerDown);

            BuildSidebar();
            var main = Element("main");
            rootVisualElement.Add(main);
            BuildHeader(main);
            chatScroll = new ScrollView(ScrollViewMode.Vertical) { name = "conversation" };
            chatScroll.AddToClassList("conversation");
            chatScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            chatScroll.RegisterCallback<WheelEvent>(_ => scrollVersion++);
            chatScroll.verticalScroller.RegisterCallback<PointerDownEvent>(_ => scrollVersion++);
            main.Add(chatScroll);
            var conversationContent = Element("conversation-content");
            chatScroll.Add(conversationContent);
            welcome = BuildWelcome();
            conversationContent.Add(welcome);
            messageList = Element("message-list");
            conversationContent.Add(messageList);
            BuildComposer(main);
            messageViews.Clear();
            renderedSessionId = null;
            historySignature = modelsSignature = null;
            Render();
            if (style == null) ShowNotice("Не найден файл оформления Chat.uss. Повторно импортируйте пакет WorldWeaver.", true);
            RefreshModels();
        }

        void BuildSidebar()
        {
            sidebar = Element("sidebar");
            rootVisualElement.Add(sidebar);
            var brand = Element("brand-row");
            brand.Add(Text("W", "brand-mark"));
            brand.Add(Text("WorldWeaver", "brand-name"));
            sidebar.Add(brand);
            newChatButton = Button("＋  Новый чат", () => SwitchChat(null), "new-chat");
            sidebar.Add(newChatButton);
            sidebar.Add(Text("ИСТОРИЯ", "section-label"));
            historyScroll = new ScrollView(ScrollViewMode.Vertical);
            historyScroll.AddToClassList("history");
            historyScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sidebar.Add(historyScroll);
            sidebar.Add(Text("Помощник для вашего\nUnity-проекта", "sidebar-caption"));
        }

        void BuildHeader(VisualElement main)
        {
            var header = Element("header");
            main.Add(header);
            var heading = Element("header-heading");
            var historyButton = Button("☰", () =>
            {
                sidebarVisible = !sidebarVisible;
                sidebar.style.display = sidebarVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }, "history-toggle");
            historyButton.tooltip = "Показать или скрыть историю чатов";
            heading.Add(historyButton);
            titleLabel = Text("Новый чат", "chat-title");
            heading.Add(titleLabel);
            undoButton = Button("Отменить действие", () => { if (EnsureEditMode()) Invoke(controller.UndoLast); }, "quiet-button");
            undoButton.tooltip = "Стандартный Undo Unity отменяет последнее действие, включая ручное.";
            heading.Add(undoButton);
            header.Add(heading);

            var controls = Element("header-controls");
            var modelGroup = Element("model-group");
            modelGroup.Add(Text("Модель помощника", "field-caption"));
            modelDropdown = new DropdownField { name = "assistant-model", tooltip = "Модель помощника и новых персонажей. Уже созданные NPC сохраняют свой назначенный маршрут." };
            modelDropdown.AddToClassList("model-dropdown");
            modelDropdown.RegisterValueChangedCallback(evt =>
            {
                if (updatingControls) return;
                int index = modelDropdown.choices.IndexOf(evt.newValue);
                if (index < 0 || index >= modelChoices.Count) return;
                controller.Settings.ModelKey = modelChoices[index].Key;
                controller.SaveSettings();
                RenderHeader();
            });
            modelGroup.Add(modelDropdown);
            controls.Add(modelGroup);
            var powerGroup = Element("power-group");
            powerGroup.Add(Text("Режим", "field-caption"));
            powerDropdown = new DropdownField(PowerLabels, 1) { name = "assistant-power", tooltip = "Быстро: 1 шаг и короткий ответ. Баланс: до 3 шагов. Глубоко: до 6 шагов и больше кода. Модель выбирается отдельно; режим не меняет её число параметров." };
            powerDropdown.RegisterValueChangedCallback(evt =>
            {
                if (updatingControls) return;
                int index = PowerLabels.IndexOf(evt.newValue);
                if (index < 0) return;
                controller.Settings.Power = (AssistantPower)index;
                controller.SaveSettings();
            });
            powerGroup.Add(powerDropdown);
            controls.Add(powerGroup);
            var connection = Element("connection");
            connectionLabel = Text("Проверка подключения", "connection-label");
            connection.Add(connectionLabel);
            refreshButton = Button("Обновить", RefreshModels, "quiet-button");
            refreshButton.tooltip = "Заново проверить доступность и каталог моделей";
            connection.Add(refreshButton);
            controls.Add(connection);
            header.Add(controls);
        }

        VisualElement BuildWelcome()
        {
            var view = Element("welcome");
            view.Add(Text("Что создадим?", "welcome-title"));
            view.Add(Text("Опишите игру, мир или персонажа", "welcome-subtitle"));
            var prompts = Element("prompt-grid");
            AddPrompt(prompts, "Создать мир", "Долина, озеро и горы по описанию", "Создай горную долину с озером в центре, горами по краям и пологой южной частью.");
            AddPrompt(prompts, "Добавить персонажа", "Личность, знания и поведение", "Создай осторожного торговца Мирона с собственной базой знаний и инвентарём кристаллов.");
            AddPrompt(prompts, "Изменить сцену", "Объекты, окружение и композиция", "Посмотри текущую сцену и предложи, как превратить её в небольшую деревню у озера.");
            AddPrompt(prompts, "Написать скрипт", "Код C# с просмотром перед импортом", "Напиши скрипт C# для плавного вращения выбранного объекта. Покажи код перед импортом в проект.");
            view.Add(prompts);
            view.Add(Text("Работаем с текущим Unity-проектом", "welcome-caption"));
            return view;
        }

        void AddPrompt(VisualElement parent, string title, string description, string prompt)
        {
            var button = Button("", () => FillComposer(prompt), "prompt-card");
            button.Add(Text(title, "prompt-title"));
            button.Add(Text(description, "prompt-description"));
            parent.Add(button);
        }

        void BuildComposer(VisualElement main)
        {
            var area = Element("composer-area");
            main.Add(area);
            notice = Element("notice");
            noticeLabel = Text("", "notice-text");
            notice.Add(noticeLabel);
            notice.Add(Button("×", () => notice.style.display = DisplayStyle.None, "notice-dismiss"));
            notice.style.display = DisplayStyle.None;
            area.Add(notice);

            var shell = Element("composer-shell");
            area.Add(shell);
            attachment = Element("attachment");
            attachmentLabel = Text("", "attachment-label");
            attachment.Add(attachmentLabel);
            var removeAttachment = Button("×", ClearAttachment, "attachment-remove");
            removeAttachment.tooltip = "Убрать контекст выделения";
            attachment.Add(removeAttachment);
            shell.Add(attachment);
            var input = Element("composer-input");
            composer = new TextField { name = "chat-composer", multiline = true, value = draft, maxLength = 16000,
                selectAllOnFocus = false, selectAllOnMouseUp = false,
                tooltip = "Enter — отправить. Shift+Enter — новая строка. Escape — остановить запрос." };
            composer.AddToClassList("composer-field");
            composer.verticalScrollerVisibility = ScrollerVisibility.Auto;
            composer.RegisterValueChangedCallback(evt =>
            {
                draft = evt.newValue;
                UpdateComposerState();
            });
            composer.RegisterCallback<KeyDownEvent>(OnComposerKey, TrickleDown.TrickleDown);
            input.Add(composer);
            placeholder = Text("Опишите, что нужно создать или изменить…", "composer-placeholder");
            placeholder.pickingMode = PickingMode.Ignore;
            input.Add(placeholder);
            shell.Add(input);
            var bottom = Element("composer-controls");
            var add = Button("＋", ToggleTaskMenu, "add-task");
            add.name = "add-task";
            add.tooltip = "Выбрать задачу или прикрепить контекст выделения";
            bottom.Add(add);
            autoApply = new Toggle("Применять изменения автоматически") { name = "auto-apply" };
            autoApply.AddToClassList("auto-apply");
            autoApply.tooltip = "Изменения сцены выполняются автоматически. Новый скрипт всегда доступен для просмотра перед импортом.";
            autoApply.RegisterValueChangedCallback(evt =>
            {
                if (updatingControls) return;
                controller.Settings.AutoApply = evt.newValue;
                controller.SaveSettings();
            });
            bottom.Add(autoApply);
            sendButton = Button("Отправить", OnSendOrStop, "send-button");
            bottom.Add(sendButton);
            shell.Add(bottom);
            var footer = Element("composer-footer");
            phaseLabel = Text("", "phase-label");
            footer.Add(phaseLabel);
            footer.Add(Text("Enter — отправить · Shift+Enter — новая строка", "keyboard-hint"));
            area.Add(footer);
            BuildTaskMenu(area);
        }

        void BuildTaskMenu(VisualElement area)
        {
            taskMenu = Element("task-menu");
            taskMenu.Add(Text("С ЧЕГО НАЧНЁМ", "section-label"));
            taskMenu.Add(Button("Мир", () => ChooseTask("Создай мир: "), "task-option"));
            taskMenu.Add(Button("Персонаж", () => ChooseTask("Создай персонажа: "), "task-option"));
            taskMenu.Add(Button("Изменить сцену", () => ChooseTask("Измени текущую сцену: "), "task-option"));
            taskMenu.Add(Button("Написать скрипт", () => ChooseTask("Напиши скрипт C# для Unity: "), "task-option"));
            taskMenu.Add(Element("task-separator"));
            taskMenu.Add(Button("Прикрепить контекст выделения", AttachSelection, "task-option"));
            taskMenu.style.display = DisplayStyle.None;
            area.Add(taskMenu);
        }

        void Render()
        {
            if (rendering || controller == null || composer == null) return;
            rendering = true;
            try
            {
                var session = controller.Current;
                if (session != null) lastSessionId = session.Id;
                bool changedSession = session != null && renderedSessionId != session.Id;
                Vector2 oldScroll = chatScroll.scrollOffset;
                bool nearBottom = chatScroll.verticalScroller.highValue - chatScroll.verticalScroller.value < 44;
                if (changedSession)
                {
                    if (!string.IsNullOrEmpty(renderedSessionId))
                    {
                        scrollPositions[renderedSessionId] = oldScroll;
                        drafts[renderedSessionId] = composer.value;
                        draft = drafts.TryGetValue(session.Id, out var savedDraft) ? savedDraft : "";
                        composer.SetValueWithoutNotify(draft);
                        ClearAttachment();
                    }
                    renderedSessionId = session.Id;
                    messageViews.Clear();
                    messageList.Clear();
                }
                RenderHeader();
                RenderHistory();
                var messages = session != null ? session.Messages : new List<AssistantMessage>();
                welcome.style.display = messages.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                bool contentChanged = changedSession;
                var ids = new HashSet<string>(messages.Select(m => m.Id));
                foreach (var obsolete in messageViews.Keys.Where(id => !ids.Contains(id)).ToArray())
                {
                    messageViews[obsolete].Root.RemoveFromHierarchy();
                    messageViews.Remove(obsolete);
                    contentChanged = true;
                }
                foreach (var message in messages)
                {
                    if (!messageViews.TryGetValue(message.Id, out var view))
                    {
                        view = new MessageView(this, message);
                        messageViews.Add(message.Id, view);
                        messageList.Add(view.Root);
                        contentChanged = true;
                    }
                    contentChanged |= view.Update(message);
                }
                UpdateComposerState();
                if (contentChanged)
                {
                    Vector2 saved = oldScroll;
                    bool bottom = nearBottom;
                    if (changedSession)
                    {
                        bottom = !scrollPositions.TryGetValue(session.Id, out saved);
                    }
                    int version = ++scrollVersion;
                    chatScroll.schedule.Execute(() =>
                    {
                        if (version != scrollVersion || chatScroll.panel == null) return;
                        chatScroll.scrollOffset = bottom ? new Vector2(0, chatScroll.verticalScroller.highValue) : saved;
                    });
                }
            }
            finally { rendering = false; }
        }

        void RenderHeader()
        {
            updatingControls = true;
            try
            {
                sidebar.style.display = sidebarVisible ? DisplayStyle.Flex : DisplayStyle.None;
                titleLabel.text = controller.Current != null ? controller.Current.Title : "Новый чат";
                var models = controller.Models ?? new List<AvailableModel>();
                string signature = string.Join("\n", models.Select(m => m.Key + ":" + m.Name + ":" + m.Available));
                if (signature != modelsSignature)
                {
                    modelsSignature = signature;
                    modelChoices.Clear();
                    modelChoices.AddRange(models);
                    var labels = modelChoices.Select(m => (string.IsNullOrEmpty(m.Name) ? m.Model : m.Name) + (m.Available ? "" : " · недоступна")).ToList();
                    for (int i = 0; i < labels.Count; i++)
                        if (labels.Count(v => v == labels[i]) > 1) labels[i] += " · " + modelChoices[i].Key;
                    modelDropdown.choices = labels.Count > 0 ? labels : new List<string> { "Модели не найдены" };
                }
                int selected = modelChoices.FindIndex(m => m.Key == controller.Settings.ModelKey);
                modelDropdown.SetValueWithoutNotify(selected >= 0 ? modelDropdown.choices[selected] : "Выберите модель");
                modelDropdown.SetEnabled(!controller.Busy && modelChoices.Count > 0);
                powerDropdown.SetValueWithoutNotify(PowerLabels[Mathf.Clamp((int)controller.Settings.Power, 0, 2)]);
                powerDropdown.SetEnabled(!controller.Busy);
                autoApply.SetValueWithoutNotify(controller.Settings.AutoApply);
                autoApply.SetEnabled(!controller.Busy);
                connectionLabel.text = refreshingModels ? "Проверка подключения…" : controller.Connected ? "Подключено" : "Нет подключения";
                connectionLabel.EnableInClassList("connected", controller.Connected);
                refreshButton.SetEnabled(!controller.Busy && !refreshingModels);
                newChatButton.SetEnabled(!controller.Busy);
                undoButton.SetEnabled(!controller.Busy && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode && !ScriptDrafts.Pending);
                phaseLabel.text = !string.IsNullOrWhiteSpace(controller.Phase) ? controller.Phase : controller.Busy ? "Запрос выполняется" : controller.Connected ? "Готов к работе" : "Выберите доступную модель или нажмите «Обновить»";
                phaseLabel.EnableInClassList("working", controller.Busy);
            }
            finally { updatingControls = false; }
        }

        void RenderHistory()
        {
            var sessions = controller.Sessions ?? new List<ChatSession>();
            string signature = renderedSessionId + ":" + controller.Busy + ":" + string.Join("\n", sessions.Select(s => s.Id + ":" + s.Title + ":" + s.UpdatedUtc));
            if (signature == historySignature) return;
            historySignature = signature;
            var scroll = historyScroll.scrollOffset;
            historyScroll.Clear();
            if (sessions.Count == 0) historyScroll.Add(Text("Здесь появятся ваши чаты", "history-empty"));
            foreach (var session in sessions.OrderByDescending(s => s.UpdatedUtc))
            {
                string id = session.Id;
                var button = Button("", () => SwitchChat(id), "history-item");
                button.EnableInClassList("selected", id == renderedSessionId);
                button.SetEnabled(!controller.Busy);
                button.tooltip = session.Title;
                button.Add(Text(string.IsNullOrWhiteSpace(session.Title) ? "Новый чат" : session.Title, "history-title"));
                if (DateTime.TryParse(session.UpdatedUtc, out var date))
                    button.Add(Text(date.ToLocalTime().ToString("dd.MM · HH:mm"), "history-date"));
                historyScroll.Add(button);
            }
            historyScroll.schedule.Execute(() => historyScroll.scrollOffset = scroll);
        }

        void UpdateComposerState()
        {
            if (composer == null || sendButton == null || controller == null) return;
            placeholder.style.display = string.IsNullOrEmpty(composer.value) ? DisplayStyle.Flex : DisplayStyle.None;
            attachment.style.display = string.IsNullOrEmpty(attachedSelection) ? DisplayStyle.None : DisplayStyle.Flex;
            attachmentLabel.text = attachmentTitle;
            attachment.tooltip = attachedSelection;
            sendButton.text = controller.Busy ? "Остановить" : "Отправить";
            sendButton.EnableInClassList("stop-button", controller.Busy);
            bool available = modelChoices.Any(m => m.Key == controller.Settings.ModelKey && m.Available);
            sendButton.SetEnabled(controller.Busy || (controller.Connected && available && !EditorApplication.isCompiling && !ScriptDrafts.Pending && !EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrWhiteSpace(composer.value)));
            sendButton.tooltip = controller.Busy ? "Остановить текущий запрос (Escape)" : "Отправить сообщение (Enter)";
        }

        void OnComposerKey(KeyDownEvent evt)
        {
            if ((evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) || evt.shiftKey || IsComposing()) return;
#if !UNITY_6000_0_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopImmediatePropagation();
            if (!controller.Busy && sendButton.enabledSelf) SendDraft();
        }

        static bool IsComposing()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return !string.IsNullOrEmpty(Input.compositionString);
#else
            return false;
#endif
        }

        void OnWindowKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            CloseTaskMenu();
            if (!controller.Busy) return;
            controller.Cancel();
            Render();
#if !UNITY_6000_0_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopPropagation();
        }

        void OnSendOrStop()
        {
            if (controller.Busy) { controller.Cancel(); Render(); }
            else SendDraft();
        }

        void SendDraft()
        {
            string text = composer.value.Trim();
            if (text.Length == 0 || controller.Busy) return;
            string payload = string.IsNullOrEmpty(attachedSelection) ? text : text + "\n\nКонтекст выделения в Unity:\n" + attachedSelection;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { ShowNotice("Выйдите из режима игры и дождитесь завершения компиляции, чтобы отправить запрос.", false); return; }
            if (payload.Length > 16000) { ShowNotice("Сообщение вместе с выделением превышает 16 000 символов. Сократите текст или уберите прикреплённый контекст.", true); return; }
            draft = "";
            composer.SetValueWithoutNotify("");
            ClearAttachment();
            CloseTaskMenu();
            notice.style.display = DisplayStyle.None;
            UpdateComposerState();
            Run(() => controller.SendAsync(payload));
            composer.Focus();
        }

        void SwitchChat(string id)
        {
            if (controller.Busy) return;
            CloseTaskMenu();
            notice.style.display = DisplayStyle.None;
            if (id == null) controller.NewChat(); else controller.OpenChat(id);
            Render();
            composer.Focus();
        }

        void FillComposer(string value)
        {
            composer.value = value;
            composer.Focus();
            composer.SelectRange(value.Length, value.Length);
        }

        void ChooseTask(string prefix)
        {
            CloseTaskMenu();
            string value = composer.value.Trim();
            FillComposer(string.IsNullOrEmpty(value) || value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? (value.Length == 0 ? prefix : value) : prefix + value);
        }

        void ToggleTaskMenu() => taskMenu.style.display = taskMenu.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        void CloseTaskMenu() { if (taskMenu != null) taskMenu.style.display = DisplayStyle.None; }

        void AttachSelection()
        {
            CloseTaskMenu();
            var selection = Selection.objects;
            if (selection.Length == 0) { ShowNotice("Сначала выделите объект в сцене или ассет в окне проекта.", false); return; }
            var lines = new List<string>();
            foreach (var item in selection.Take(32))
            {
                string asset = AssetDatabase.GetAssetPath(item);
                if (!string.IsNullOrEmpty(asset)) lines.Add("Ассет: " + asset);
                else if (item is GameObject go)
                {
                    string hierarchy = go.name;
                    for (var parent = go.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
                    lines.Add("Объект сцены: " + hierarchy);
                }
                else lines.Add("Выделенный объект: " + item.name);
            }
            attachedSelection = string.Join("\n", lines);
            attachmentTitle = selection.Length == 1 ? "Контекст: " + selection[0].name : "Объектов в контексте: " + Math.Min(selection.Length, 32);
            UpdateComposerState();
            composer.Focus();
        }

        void ClearAttachment()
        {
            attachedSelection = attachmentTitle = "";
            if (attachment != null) attachment.style.display = DisplayStyle.None;
        }

        string OriginalRequest(string messageId)
        {
            var messages = controller.Current.Messages;
            int index = messages.FindIndex(m => m.Id == messageId);
            for (int i = index - 1; i >= 0; i--) if (messages[i].Role == "user") return messages[i].Text;
            return null;
        }

        void Retry(string messageId)
        {
            string text = OriginalRequest(messageId);
            if (controller.Busy || string.IsNullOrEmpty(text)) return;
            notice.style.display = DisplayStyle.None;
            Run(() => controller.SendAsync(text));
        }

        void ApplyPlan(string messageId)
        {
            if (!EnsureEditMode() || controller.Busy) return;
            Run(() => controller.ApplyPendingAsync(messageId));
        }

        void RevertCode(string messageId)
        {
            if (!EnsureEditMode() || controller.Busy) return;
            if (ScriptDrafts.Pending) { ShowNotice("Дождитесь текущей проверки кода, чтобы восстановить предыдущую версию.", false); return; }
            Invoke(() => controller.RevertCode(messageId));
        }

        bool EnsureEditMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !ScriptDrafts.Pending) return true;
            ShowNotice("Выйдите из режима игры и дождитесь завершения компиляции, чтобы изменить проект.", false);
            return false;
        }

        async void Run(Func<Task> operation)
        {
            int version = lifecycleVersion;
            try { await operation(); }
            catch (OperationCanceledException) { if (version == lifecycleVersion && notice != null) ShowNotice("Запрос отменён.", false); }
            catch (Exception ex) { if (version == lifecycleVersion && notice != null) ShowNotice("Не удалось выполнить запрос: " + ex.Message, true); }
            finally { if (version == lifecycleVersion && controller != null && composer != null) Render(); }
        }

        void RefreshModels()
        {
            if (refreshingModels || controller == null) return;
            refreshingModels = true;
            var active = controller;
            int version = lifecycleVersion;
            RenderHeader();
            Run(async () =>
            {
                try { await active.RefreshModelsAsync(); }
                finally
                {
                    if (version == lifecycleVersion && controller == active)
                    { refreshingModels = false; MarkChanged(); }
                }
            });
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            var target = evt.target as VisualElement;
            if (taskMenu == null || target == null) return;
            bool inMenu = target == taskMenu || taskMenu.Contains(target);
            bool onAdd = target.name == "add-task" || target.GetFirstAncestorOfType<Button>()?.name == "add-task";
            if (!inMenu && !onAdd) CloseTaskMenu();
        }

        void Invoke(Action operation)
        {
            try { operation(); Render(); }
            catch (Exception ex) { ShowNotice("Не удалось выполнить действие: " + ex.Message, true); }
        }

        void ShowNotice(string text, bool error)
        {
            noticeLabel.text = text;
            notice.EnableInClassList("notice-error", error);
            notice.style.display = DisplayStyle.Flex;
        }

        static VisualElement Element(string className) { var element = new VisualElement(); element.AddToClassList(className); return element; }
        static Label Text(string value, string className)
        {
            var label = new Label(value) { enableRichText = false };
            label.AddToClassList("chat-text");
            label.AddToClassList(className);
            return label;
        }
        static Button Button(string label, Action action, string className) { var button = new Button(action) { text = label }; button.AddToClassList(className); return button; }

        sealed class MessageView
        {
            readonly WorldWeaverChatWindow owner;
            readonly Label body, status, metadata;
            readonly VisualElement steps, review;
            readonly Button retry, apply, revertCode;
            AssistantMessage message;
            string stepsSignature, planSignature, textSignature;
            bool revertVisible;
            public VisualElement Root { get; }

            public MessageView(WorldWeaverChatWindow owner, AssistantMessage value)
            {
                this.owner = owner;
                message = value;
                Root = Element("message");
                Root.EnableInClassList("user-message", value.Role == "user");
                Root.EnableInClassList("assistant-message", value.Role != "user");
                var heading = Element("message-heading");
                heading.Add(Text(value.Role == "user" ? "Вы" : value.Role == "system" ? "Система" : "WorldWeaver", "message-author"));
                metadata = Text("", "message-metadata");
                heading.Add(metadata);
                Root.Add(heading);
                body = Text("", "message-body");
                body.selection.isSelectable = true;
                Root.Add(body);
                status = Text("", "message-status");
                Root.Add(status);
                steps = Element("message-steps");
                Root.Add(steps);
                review = Element("plan-review");
                Root.Add(review);
                var actions = Element("message-actions");
                actions.Add(Button("Копировать", () => { EditorGUIUtility.systemCopyBuffer = message.Text ?? ""; }, "message-action"));
                retry = Button("Повторить", () => owner.Retry(message.Id), "message-action");
                retry.style.display = value.Role == "assistant" ? DisplayStyle.Flex : DisplayStyle.None;
                actions.Add(retry);
                apply = Button("Применить план", () => owner.ApplyPlan(message.Id), "apply-plan");
                actions.Add(apply);
                revertCode = Button("Вернуть предыдущий код", () => owner.RevertCode(message.Id), "message-action");
                revertCode.tooltip = "Восстановить файлы C# до этого импорта. Unity повторно проверит компиляцию; ручные правки после импорта сохраняются.";
                revertCode.style.display = DisplayStyle.None;
                actions.Add(revertCode);
                Root.Add(actions);
            }

            public bool Update(AssistantMessage value)
            {
                message = value;
                string content = (value.Text ?? "") + "\n" + value.Status + "\n" + value.Model + "\n" + value.ElapsedMs + ":" + value.IsError;
                bool changed = content != textSignature;
                textSignature = content;
                string empty = owner.controller.Busy ? "Изучаю запрос…" :
                    value.Status != null && value.Status.IndexOf("отмен", StringComparison.OrdinalIgnoreCase) >= 0 ? "Запрос отменён." : "Ответ ещё не получен.";
                body.text = string.IsNullOrEmpty(value.Text) ? empty : value.Text;
                status.text = HumanStatus(value.Status);
                status.style.display = string.IsNullOrEmpty(value.Status) ? DisplayStyle.None : DisplayStyle.Flex;
                Root.EnableInClassList("error-message", value.IsError);
                metadata.text = value.Role == "user" ? "" : string.Join(" · ", new[] { value.Model, value.ElapsedMs > 0 ? (value.ElapsedMs / 1000d).ToString("0.0") + " с" : null }.Where(s => !string.IsNullOrEmpty(s)));
                retry.SetEnabled(!owner.controller.Busy && !string.IsNullOrEmpty(owner.OriginalRequest(value.Id)));
                apply.style.display = value.PendingPlan == null ? DisplayStyle.None : DisplayStyle.Flex;
                apply.SetEnabled(!owner.controller.Busy && !EditorApplication.isCompiling && !ScriptDrafts.Pending && !EditorApplication.isPlayingOrWillChangePlaymode);
                bool canRevert = value.Role == "assistant" && value.Steps != null &&
                    value.Steps.Any(step => step.Tool == "draft_script" && step.Success) &&
                    ScriptDrafts.CanRevert(owner.controller.Current.Id, value.Id);
                if (revertVisible != canRevert) { revertVisible = canRevert; changed = true; }
                revertCode.style.display = canRevert ? DisplayStyle.Flex : DisplayStyle.None;
                revertCode.SetEnabled(canRevert && !owner.controller.Busy && !EditorApplication.isCompiling &&
                    !EditorApplication.isPlayingOrWillChangePlaymode && !ScriptDrafts.Pending);
                string signature = string.Join("\n", (value.Steps ?? new List<EditorCommandResult>()).Select(s => s.Success + ":" + s.Summary + ":" + s.Target + ":" + s.Error));
                if (signature != stepsSignature)
                {
                    stepsSignature = signature;
                    RenderSteps(value.Steps);
                    changed = true;
                }
                string pending = value.PendingPlan == null ? "" : value.PendingPlan.reply + "\n" + string.Join("\n", value.PendingPlan.actions.Select(a => a.tool + ":" + a.name + ":" + a.target + ":" + a.args));
                if (pending != planSignature)
                {
                    planSignature = pending;
                    RenderPlan(value.PendingPlan);
                    changed = true;
                }
                return changed;
            }

            void RenderSteps(List<EditorCommandResult> values)
            {
                bool expanded = steps.Q<Foldout>() != null ? steps.Q<Foldout>().value : values != null && values.Any(v => !v.Success);
                steps.Clear();
                steps.style.display = values == null || values.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (values == null || values.Count == 0) return;
                var foldout = new Foldout { text = "Результаты действий · " + values.Count, value = expanded };
                foreach (var value in values)
                {
                    var row = Element("step-result");
                    row.Add(Text(value.Success ? "Готово" : "Ошибка", value.Success ? "step-success" : "step-failure"));
                    string description = HumanTool(value.Tool);
                    GameObject target = null;
                    if (value.Success && !string.IsNullOrWhiteSpace(value.Target))
                        try { target = SceneToolExecutor.ResolveTarget(value.Target); } catch (ArgumentException) { }
                    if (target) description += " · «" + target.name + "»";
                    row.Add(Text(description, "step-description"));
                    string summary = value.Summary ?? "";
                    if (!string.IsNullOrWhiteSpace(summary) && !summary.StartsWith("{") && !summary.StartsWith("["))
                        row.Add(Text(summary, "step-assets"));
                    if (target)
                    {
                        var actual = target;
                        row.Add(Button("Показать в сцене", () =>
                        {
                            if (!actual) { owner.ShowNotice("Объект больше не находится в сцене.", false); return; }
                            Selection.activeGameObject = actual;
                            var sceneView = GetWindow<SceneView>();
                            sceneView.FrameSelected(); sceneView.Focus();
                        }, "message-action"));
                    }
                    if (value.CreatedAssets != null && value.CreatedAssets.Count > 0)
                    {
                        string assets = "Ассеты: " + string.Join(", ", value.CreatedAssets.Take(8));
                        if (value.CreatedAssets.Count > 8) assets += " и ещё " + (value.CreatedAssets.Count - 8);
                        var details = new Foldout { text = "Созданные файлы · " + value.CreatedAssets.Count, value = false };
                        details.Add(Text(assets, "step-assets")); row.Add(details);
                    }
                    if (!string.IsNullOrEmpty(value.Error)) row.Add(Text(value.Error, "step-error"));
                    foldout.Add(row);
                }
                steps.Add(foldout);
            }

            void RenderPlan(EditorPlan plan)
            {
                review.Clear();
                review.style.display = plan == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (plan == null) return;
                review.Add(Text("План ждёт подтверждения", "review-title"));
                foreach (var action in plan.actions)
                {
                    if (action.tool == "draft_script")
                    {
                        string name = string.IsNullOrWhiteSpace(action.name) ? "НовыйСкрипт" : action.name;
                        if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) name += ".cs";
                        var foldout = new Foldout { text = "Код · " + name, value = true };
                        string description = action.args?["description"]?.ToString();
                        if (!string.IsNullOrEmpty(description)) foldout.Add(Text(description, "review-description"));
                        string code = action.args?["code"]?.ToString() ?? "";
                        var field = new TextField { multiline = true, isReadOnly = true, value = code, selectAllOnFocus = false, selectAllOnMouseUp = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
                        field.AddToClassList("code-preview");
                        foldout.Add(field);
                        foldout.Add(Button("Копировать код", () => EditorGUIUtility.systemCopyBuffer = code, "message-action"));
                        review.Add(foldout);
                        review.Add(Text("Скрипт попадёт в проект после «Применить план». Результат импорта и компиляции появится в чате.", "review-caption"));
                    }
                    else
                    {
                        string title = HumanTool(action.tool);
                        if (!string.IsNullOrEmpty(action.name)) title += " · " + action.name;
                        if (!string.IsNullOrEmpty(action.target)) title += " · «" + action.target + "»";
                        review.Add(Text(title, "review-action"));
                    }
                }
            }

            static string HumanStatus(string status)
            {
                if (string.IsNullOrEmpty(status)) return "";
                switch (status.ToLowerInvariant())
                {
                    case "done": case "complete": case "completed": return "Готово";
                    case "error": case "failed": return "Ошибка";
                    case "cancelled": case "canceled": return "Отменено";
                    case "pending": return "Ожидание";
                    case "thinking": return "Подготовка ответа";
                    default: return status;
                }
            }

            static string HumanTool(string tool)
            {
                switch (tool)
                {
                    case "create_world": return "Создать мир";
                    case "update_world": return "Изменить ландшафт и окружение";
                    case "create_terrain": case "generate_terrain": return "Создать рельеф";
                    case "create_npc": return "Создать персонажа";
                    case "update_npc": return "Изменить персонажа";
                    case "create_object": case "create_primitive": return "Создать объект";
                    case "set_transform": case "transform_object": return "Изменить положение объекта";
                    case "set_material": case "create_material": return "Настроить материал";
                    case "add_component": return "Добавить компонент";
                    case "set_property": return "Изменить свойство";
                    case "create_scene": return "Создать сцену";
                    case "scatter_objects": return "Разместить объекты";
                    case "set_lighting": return "Настроить освещение";
                    case "create_camera": return "Создать камеру";
                    case "attach_behavior": return "Добавить поведение";
                    case "delete_object": return "Удалить объект";
                    case "save_scene": return "Сохранить сцену";
                    case "inspect_object": return "Изучить объект";
                    case "search_project": return "Найти ассеты в проекте";
                    case "read_script": return "Прочитать скрипт";
                    case "draft_script": return "Подготовить скрипт";
                    case "attach_script": return "Добавить скрипт к объекту";
                    default: return "Действие с объектом";
                }
            }
        }
    }
}
