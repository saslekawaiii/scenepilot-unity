using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        [SerializeField] List<ChatAttachment> draftImages = new List<ChatAttachment>();

        ChatController controller;
        readonly Dictionary<string, MessageView> messageViews = new Dictionary<string, MessageView>();
        readonly Dictionary<string, Vector2> scrollPositions = new Dictionary<string, Vector2>();
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, List<ChatAttachment>> imageDrafts = new Dictionary<string, List<ChatAttachment>>();
        readonly List<AvailableModel> modelChoices = new List<AvailableModel>();
        readonly Dictionary<AssistantMode, Button> modeButtons = new Dictionary<AssistantMode, Button>();
        readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        AgentSkillCatalog skillCatalog;
        string skillFoldersSignature;
        VisualElement sidebar, messageList, welcome, taskMenu, notice, attachment, mainPane, historyScrim;
        VisualElement imageAttachments, skillsPanel, skillResults;
        TextField skillSearch, skillBody;
        ScrollView chatScroll, historyScroll;
        TextField composer;
        Label phaseLabel, connectionLabel, titleLabel, noticeLabel, attachmentLabel, placeholder;
        Label modeHint, imageHint, skillDescription;
        Button sendButton, refreshButton, newChatButton, undoButton, skillsButton, addApiButton;
        DropdownField modelDropdown, powerDropdown;
        Toggle autoApply;
        string renderedSessionId, historySignature, modelsSignature;
        bool updatingControls, rendering, refreshingModels;
        bool compactLayout, historyDrawerOpen;
        bool lastEditorCompiling, lastEditorPlaying;
        bool backgroundBlocked;
        int lifecycleVersion;
        int needsRefresh, scrollVersion;
        double nextElapsedRefresh;
        double nextBackgroundRefresh;

        [MenuItem("Tools/WorldWeaver/Чат", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldWeaverChatWindow>();
            window.titleContent = new GUIContent("WorldWeaver · Чат");
            window.minSize = new Vector2(420, 480);
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
            ReleaseThumbnails();
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
            if (EditorApplication.timeSinceStartup >= nextBackgroundRefresh)
            {
                nextBackgroundRefresh = EditorApplication.timeSinceStartup + .5;
                bool blocked = ScriptDrafts.Pending || AgentSourcePatches.Pending || AgentEditorJobs.Busy;
                if (blocked != backgroundBlocked) { backgroundBlocked = blocked; MarkChanged(); }
            }
            if (Interlocked.Exchange(ref needsRefresh, 0) != 0 && composer != null) Render();
            if (controller?.Busy == true && phaseLabel != null && EditorApplication.timeSinceStartup >= nextElapsedRefresh)
            {
                nextElapsedRefresh = EditorApplication.timeSinceStartup + 1;
                var currentReply = controller.Current?.Messages.LastOrDefault(message => message.Role == "assistant");
                if (currentReply != null && DateTime.TryParse(currentReply.CreatedUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var started))
                {
                    long seconds = Math.Max(0, (long)(DateTime.UtcNow - started.ToUniversalTime()).TotalSeconds);
                    phaseLabel.text = controller.Phase + " · " + (seconds / 60) + ":" + (seconds % 60).ToString("D2");
                }
            }
        }

        public void CreateGUI()
        {
            EnsureController();
            minSize = new Vector2(420, 480);
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
            rootVisualElement.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            rootVisualElement.UnregisterCallback<DragPerformEvent>(OnDragPerform);
            rootVisualElement.RegisterCallback<DragPerformEvent>(OnDragPerform);
            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);

            BuildSidebar();
            var main = Element("main");
            mainPane = main; main.name = "chat-main";
            rootVisualElement.Add(main);
            BuildHeader(main);
            chatScroll = new ScrollView(ScrollViewMode.Vertical) { name = "conversation" };
            chatScroll.AddToClassList("conversation");
            chatScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // Unity6 inserts a wrapper around the viewport; bind our own class to the actual container.
            chatScroll.contentContainer.AddToClassList("conversation-stack");
            // Overlay the scroller in the column gutter so a long chat keeps the same centre as the composer.
            chatScroll.verticalScroller.style.position = Position.Absolute;
            chatScroll.verticalScroller.style.right = 2;
            chatScroll.verticalScroller.style.top = 0;
            chatScroll.verticalScroller.style.bottom = 0;
            chatScroll.RegisterCallback<WheelEvent>(_ => scrollVersion++);
            chatScroll.verticalScroller.RegisterCallback<PointerDownEvent>(_ => scrollVersion++);
            main.Add(chatScroll);
            var conversationContent = Element("conversation-content");
            conversationContent.name = "chat-content-column";
            conversationContent.AddToClassList("chat-column");
            chatScroll.Add(conversationContent);
            welcome = BuildWelcome();
            conversationContent.Add(welcome);
            messageList = Element("message-list");
            conversationContent.Add(messageList);
            BuildComposer(main);
            historyScrim = Element("history-scrim");
            historyScrim.style.display = DisplayStyle.None;
            historyScrim.RegisterCallback<PointerDownEvent>(evt => { SetSidebarVisible(false); evt.StopPropagation(); });
            rootVisualElement.Add(historyScrim);
            BuildSkillsPanel();
            messageViews.Clear();
            renderedSessionId = null;
            historySignature = modelsSignature = null;
            UpdateResponsiveLayout();
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
            var closeHistory = Button("×", () => SetSidebarVisible(false), "history-close");
            closeHistory.tooltip = "Закрыть историю";
            brand.Add(closeHistory);
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
            var historyButton = Button("☰", () => SetSidebarVisible(!(compactLayout ? historyDrawerOpen : sidebarVisible)), "history-toggle");
            historyButton.tooltip = "Показать или скрыть историю чатов";
            heading.Add(historyButton);
            titleLabel = Text("Новый чат", "chat-title");
            heading.Add(titleLabel);
            undoButton = Button("Отменить действие", () => { if (EnsureEditMode()) Invoke(controller.UndoLast); }, "quiet-button");
            undoButton.tooltip = "Стандартный Undo Unity отменяет последнее действие, включая ручное.";
            heading.Add(undoButton);
            skillsButton = Button("Навыки", () => OpenSkills(), "quiet-button");
            skillsButton.name = "open-skills";
            skillsButton.tooltip = "Библиотека знаний помощника и дополнительные настройки";
            heading.Add(skillsButton);
            header.Add(heading);

            var controls = Element("header-controls");
            var modelGroup = Element("model-group");
            var modelHeading = Element("model-heading");
            modelHeading.Add(Text("Модель помощника", "field-caption"));
            addApiButton = Button("Добавить API…", () => ChatModelConnectionWindow.Open(controller), "add-api");
            addApiButton.name = "add-model-api";
            addApiButton.tooltip = "Подключить совместимый API со своим адресом и моделью";
            modelHeading.Add(addApiButton); modelGroup.Add(modelHeading);
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
            powerGroup.Add(Text("Глубина", "field-caption"));
            powerDropdown = new DropdownField(PowerLabels, 1) { name = "assistant-power", tooltip = "Быстро: 1 шаг, до 2 минут. Баланс: до 3 шагов, до 4 минут. Глубоко: до 6 шагов, до 8 минут. Это верхние пределы ожидания; время зависит от модели. Режим не меняет число её параметров." };
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
            view.name = "chat-welcome";
            view.Add(Text("Что создадим?", "welcome-title"));
            view.Add(Text("Опишите игру, мир или персонажа", "welcome-subtitle"));
            var prompts = Element("prompt-grid");
            AddPrompt(prompts, "Создать мир", "Долина, озеро и горы по описанию", "Создай горную долину с озером в центре, горами по краям и пологой южной частью.");
            AddPrompt(prompts, "Добавить персонажа", "Личность, знания и поведение", "Создай осторожного торговца Мирона с собственной базой знаний и инвентарём кристаллов.");
            AddPrompt(prompts, "Играбельная деревня", "Дороги, игрок, NPC и квест", "Создай игровую деревню на 5 домов с дорогами, площадью, северными воротами, управляемым игроком и квестом доставки письма. Проверь навигацию.");
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

        void OnWindowGeometryChanged(GeometryChangedEvent evt)
        {
            if (!Mathf.Approximately(evt.oldRect.width, evt.newRect.width)) UpdateResponsiveLayout();
        }

        void SetSidebarVisible(bool visible)
        {
            if (compactLayout) historyDrawerOpen = visible;
            else sidebarVisible = visible;
            UpdateResponsiveLayout();
        }

        void UpdateResponsiveLayout()
        {
            if (sidebar == null || mainPane == null) return;
            float width = rootVisualElement.contentRect.width;
            if (float.IsNaN(width) || float.IsInfinity(width) || width < 1) width = position.width;
            bool compact = width < 900;
            if (compact != compactLayout) historyDrawerOpen = false;
            compactLayout = compact;
            bool shown = compact ? historyDrawerOpen : sidebarVisible;
            rootVisualElement.EnableInClassList("compact", compact);
            rootVisualElement.EnableInClassList("narrow", width - (!compact && shown ? 224 : 0) < 640);
            sidebar.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (historyScrim != null) historyScrim.style.display = compact && shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (compact && shown) { historyScrim?.BringToFront(); sidebar.BringToFront(); }
            else if (!compact) sidebar.SendToBack();
        }

        void BuildComposer(VisualElement main)
        {
            var area = Element("composer-area");
            area.name = "chat-composer-column";
            area.AddToClassList("chat-column");
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
            imageAttachments = Element("image-attachments");
            shell.Add(imageAttachments);
            imageHint = Text("", "image-hint");
            shell.Add(imageHint);
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
            add.tooltip = "Изображение, снимок сцены, выделение или готовая задача";
            bottom.Add(add);
            var modes = Element("mode-picker");
            modeButtons.Clear();
            foreach (var mode in new[] { AssistantMode.Ask, AssistantMode.Plan, AssistantMode.Execute })
            {
                var selectedMode = mode;
                string label = mode == AssistantMode.Ask ? "Спросить" : mode == AssistantMode.Plan ? "Спланировать" : "Выполнить";
                var button = Button(label, () => { controller.Settings.Mode = selectedMode; controller.SaveSettings(); RenderHeader(); }, "mode-option");
                button.name = "mode-" + mode.ToString().ToLowerInvariant();
                button.tooltip = mode == AssistantMode.Ask ? "Изучить проект и ответить без изменений" : mode == AssistantMode.Plan ? "Показать план изменений для просмотра" : "Выполнить задачу и проверить результат";
                modeButtons.Add(mode, button); modes.Add(button);
            }
            bottom.Add(modes);
            sendButton = Button("Отправить", OnSendOrStop, "send-button");
            bottom.Add(sendButton);
            shell.Add(bottom);
            modeHint = Text("", "mode-hint");
            shell.Add(modeHint);
            var footer = Element("composer-footer");
            phaseLabel = Text("", "phase-label");
            footer.Add(phaseLabel);
            footer.Add(Text("Enter — отправить · Shift+Enter — новая строка", "keyboard-hint"));
            area.Add(footer);
            BuildTaskMenu(area);
            RenderDraftImages();
        }

        void BuildTaskMenu(VisualElement area)
        {
            taskMenu = Element("task-menu");
            taskMenu.Add(Text("С ЧЕГО НАЧНЁМ", "section-label"));
            taskMenu.Add(Button("Мир", () => ChooseTask("Создай мир: "), "task-option"));
            taskMenu.Add(Button("Персонаж", () => ChooseTask("Создай персонажа: "), "task-option"));
            taskMenu.Add(Button("Играбельная деревня", () => ChooseTask("Создай игровую деревню с игроком и квестом: "), "task-option"));
            taskMenu.Add(Button("Изменить сцену", () => ChooseTask("Измени текущую сцену: "), "task-option"));
            taskMenu.Add(Button("Написать скрипт", () => ChooseTask("Напиши скрипт C# для Unity: "), "task-option"));
            taskMenu.Add(Element("task-separator"));
            taskMenu.Add(Button("Прикрепить изображение…", ImportImage, "task-option"));
            taskMenu.Add(Button("Сделать снимок сцены", CaptureScene, "task-option"));
            taskMenu.Add(Button("Прикрепить контекст выделения", AttachSelection, "task-option"));
            taskMenu.style.display = DisplayStyle.None;
            area.Add(taskMenu);
        }

        void EnsureSkillCatalog()
        {
            string folders = string.Join("\n", controller.Settings.SkillFolders ?? new List<string>());
            if (skillCatalog != null && skillFoldersSignature == folders) return;
            skillFoldersSignature = folders;
            skillCatalog = AgentSkillCatalog.CreateDefault(controller.Settings.SkillFolders);
        }

        void BuildSkillsPanel()
        {
            skillsPanel = Element("skills-panel");
            var heading = Element("skills-heading");
            heading.Add(Text("Навыки помощника", "panel-title"));
            heading.Add(Button("×", () => skillsPanel.style.display = DisplayStyle.None, "notice-dismiss"));
            skillsPanel.Add(heading);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("skills-content");
            skillsPanel.Add(scroll);
            scroll.Add(Text("Помощник подбирает подходящие знания под запрос. Навыки не запускают код и не добавляют новые разрешения.", "panel-caption"));
            skillSearch = new TextField { name = "skill-search", tooltip = "Поиск по названию, описанию и ключевым словам" };
            skillSearch.AddToClassList("skill-search");
            skillSearch.RegisterValueChangedCallback(_ => RefreshSkillResults());
            scroll.Add(skillSearch);
            var results = new ScrollView(ScrollViewMode.Vertical);
            results.AddToClassList("skill-results-scroll");
            skillResults = Element("skill-results"); results.Add(skillResults); scroll.Add(results);
            skillDescription = Text("Выберите навык, чтобы прочитать его подход", "skill-description");
            scroll.Add(skillDescription);
            skillBody = new TextField { name = "skill-body", multiline = true, isReadOnly = true, selectAllOnFocus = false, selectAllOnMouseUp = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
            skillBody.AddToClassList("skill-body");
            scroll.Add(skillBody);
            var advanced = new Foldout { text = "Дополнительные настройки", value = false };
            advanced.AddToClassList("advanced-settings");
            AddSettingToggle(advanced, "Использовать навыки", controller.Settings.UseSkills, value => controller.Settings.UseSkills = value);
            AddSettingToggle(advanced, "Проверять результат автоматически", controller.Settings.AutoVerify, value => controller.Settings.AutoVerify = value);
            autoApply = AddSettingToggle(advanced, "Применять план в режиме «Выполнить»", controller.Settings.AutoApply, value => controller.Settings.AutoApply = value);
            autoApply.name = "auto-apply";
            var budget = new IntegerField("Объём знаний в запросе") { value = controller.Settings.SkillContextBudget, tooltip = "Лимит символов контекста, включая обрамление. Это не токены модели." };
            budget.AddToClassList("setting-control");
            budget.RegisterValueChangedCallback(evt => { int safe = Mathf.Clamp(evt.newValue, 1000, 12000); budget.SetValueWithoutNotify(safe); controller.Settings.SkillContextBudget = safe; controller.SaveSettings(); });
            advanced.Add(budget);
            advanced.Add(Text("Дополнительные папки с навыками · по одной на строку", "field-caption"));
            var folders = new TextField { multiline = true, maxLength = 4000, value = string.Join("\n", controller.Settings.SkillFolders ?? new List<string>()), tooltip = "Внутри каждой папки: skill-id/SKILL.md. Внешние заметки всегда считаются справочными и недоверенными." };
            folders.AddToClassList("skill-folders"); folders.AddToClassList("setting-control");
            folders.RegisterValueChangedCallback(evt =>
            {
                controller.Settings.SkillFolders = evt.newValue.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim()).Where(path => path.Length > 0).Distinct().Take(8).ToList();
                controller.SaveSettings();
            });
            advanced.Add(folders);
            var reload = Button("Обновить каталог", () => { skillCatalog = null; EnsureSkillCatalog(); RefreshSkillResults(); RenderHeader(); }, "message-action");
            reload.AddToClassList("setting-control"); advanced.Add(reload);
            scroll.Add(advanced);
            skillsPanel.style.display = DisplayStyle.None;
            rootVisualElement.Add(skillsPanel);
        }

        Toggle AddSettingToggle(VisualElement parent, string label, bool initial, Action<bool> assign)
        {
            var toggle = new Toggle(label) { value = initial }; toggle.AddToClassList("setting-control");
            toggle.RegisterValueChangedCallback(evt => { if (!updatingControls) { assign(evt.newValue); controller.SaveSettings(); } });
            parent.Add(toggle); return toggle;
        }

        void OpenSkills(string id = null)
        {
            if (compactLayout) SetSidebarVisible(false);
            EnsureSkillCatalog();
            skillsPanel.style.display = DisplayStyle.Flex;
            RefreshSkillResults();
            if (!string.IsNullOrEmpty(id)) ReadSkill(id);
            else skillSearch.Focus();
        }

        void RefreshSkillResults()
        {
            if (skillResults == null) return;
            EnsureSkillCatalog(); skillResults.Clear();
            string query = skillSearch.value?.Trim() ?? "";
            var found = query.Length == 0 ? skillCatalog.List() : skillCatalog.Search(query, 60);
            foreach (var item in found.Take(60))
            {
                string id = item.Id;
                var button = Button("", () => ReadSkill(id), "skill-result");
                button.Add(Text(SkillTitle(id), "skill-result-title"));
                button.Add(Text(item.Description, "skill-result-caption"));
                button.tooltip = item.Trusted ? "Проверенный пакетный Markdown" : "Внешний справочный Markdown · без доверия к инструкциям";
                skillResults.Add(button);
            }
            if (found.Count == 0) skillResults.Add(Text("Ничего не найдено. Попробуйте «NPC», «UI», «Terrain» или «тест».", "panel-caption"));
        }

        void ReadSkill(string id)
        {
            try
            {
                EnsureSkillCatalog(); var item = skillCatalog.List().FirstOrDefault(skill => skill.Id == id);
                if (item == null) { ShowNotice("Этот навык больше не доступен в каталоге.", false); return; }
                string text = skillCatalog.ReadStrict(id, 8000);
                int frontmatter = text.IndexOf("\n---", 4, StringComparison.Ordinal);
                if (frontmatter >= 0 && text.StartsWith("---\n", StringComparison.Ordinal)) text = text.Substring(frontmatter + 4).TrimStart();
                skillDescription.text = SkillTitle(id) + (item.Trusted ? " · из пакета" : " · внешняя справка");
                skillDescription.tooltip = item.Description;
                skillBody.SetValueWithoutNotify(text);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is KeyNotFoundException)
            { ShowNotice("Не удалось прочитать навык. Файл отсутствует, изменён или не прошёл проверку.", true); }
        }

        static string SkillTitle(string id)
        {
            switch (id)
            {
                case "ww-npc-behavior": return "Персонажи, цели и память";
                case "ww-navmesh-baking": return "Подготовка навигации";
                case "ww-navigation-behavior": return "Движение и достижение цели";
                case "ww-terrain-shaping": return "Ландшафт и высоты";
                case "ww-procedural-generation": return "Генерация мира";
                case "ww-ui-toolkit": return "UI Toolkit";
                case "ww-ugui-layout": return "Игровые экраны UGUI";
                case "ww-ui-screen-flow": return "Понятный интерфейс";
                case "ww-script-review": return "Проверка C#";
                case "ww-test-runner": return "Тестирование";
                case "ww-save-load": return "Сохранение и загрузка";
                case "ww-quests-level-events": return "Квесты и события";
                case "ww-lighting": return "Освещение";
                case "ww-materials": return "Материалы";
                case "ww-build-platforms": return "Сборка игры";
                case "ww-debug-reproduction": return "Диагностика ошибок";
                case "ww-undo-journal": return "Откат изменений";
                case "ww-task-workflow": return "Планирование задачи";
                case "ww-architecture": return "Архитектура игры";
                case "ww-patterns": return "Выбор паттерна";
                case "ww-script-roles": return "Роли классов";
                case "ww-async-lifetime": return "Асинхронность и отмена";
                case "ww-assembly-boundaries": return "Сборки asmdef";
                case "ww-scriptable-config": return "Конфигурация ScriptableObject";
                case "ww-inspector-authoring": return "Понятный Inspector";
                case "ww-scene-contracts": return "Контракт сцены";
                case "ww-scene-objects": return "Объекты и иерархия";
                case "ww-components": return "Компоненты и свойства";
                case "ww-asset-lifecycle": return "Ресурсы и их ссылки";
                case "ww-import-settings": return "Настройки импорта";
                case "ww-prefab-editing": return "Применение префабов";
                case "ww-shader-code": return "Шейдеры HLSL";
                case "ww-shadergraph-design": return "Shader Graph";
                case "ww-graphics-settings": return "Настройки графики";
                case "ww-urp-versioning": return "Версии URP";
                case "ww-post-processing": return "Постобработка";
                case "ww-camera-framing": return "Кадрирование камеры";
                case "ww-cinemachine": return "Камеры Cinemachine";
                case "ww-animator-states": return "Animator и переходы";
                case "ww-timeline": return "Катсцены Timeline";
                case "ww-physics-behavior": return "Физика и столкновения";
                case "ww-asset-optimization": return "Оптимизация ресурсов";
                case "ww-runtime-performance": return "Производительность";
                case "ww-profiler-evidence": return "Измерения в Profiler";
                case "ww-testability": return "Проверяемая логика";
                case "ww-console-triage": return "Ошибки Console";
                case "ww-project-scout": return "Обзор проекта";
                case "ww-scene-validation": return "Целостность сцены";
                case "ww-package-compatibility": return "Пакеты и совместимость";
                case "ww-addressables-design": return "Загрузка ресурсов";
                case "ww-architecture-decisions": return "Решения и компромиссы";
                case "ww-state-machines": return "Автоматы поведения";
                case "ww-game-loop": return "Игровая сессия";
                case "ww-input-correctness": return "Клавиатура и мышь";
                case "ww-audio-spatial": return "Пространственный звук";
                case "ww-lifecycle-correctness": return "Жизненный цикл";
                case "ww-data-versioning": return "Форматы и миграции";
                case "ww-editor-tools": return "Инструменты редактора";
                case "ww-physics-queries": return "Лучи и пересечения";
                case "ww-unity-foundations": return "Основы Unity";
                default: return (id ?? "Навык").Replace("ww-", "").Replace('-', ' ');
            }
        }

        void ImportImage()
        {
            CloseTaskMenu(); if (controller.HasPendingWork) return;
            string path = EditorUtility.OpenFilePanelWithFilters("Изображение для помощника", "", new[] { "Изображения", "png,jpg,jpeg" });
            if (!string.IsNullOrEmpty(path)) AddImage(() => AgentViewCapture.Import(path));
        }
        void CaptureScene() { CloseTaskMenu(); if (!controller.HasPendingWork) AddImage(() => AgentViewCapture.Capture()); }
        void AddImage(Func<ChatAttachment> create)
        {
            if (draftImages == null) draftImages = new List<ChatAttachment>();
            if (draftImages.Count >= 3) { ShowNotice("В один запрос можно добавить до трёх изображений. Уберите одно, чтобы добавить другое.", false); return; }
            try
            {
                var image = create();
                if (draftImages.Any(existing => existing.Sha256 == image.Sha256)) { ShowNotice("Это изображение уже прикреплено.", false); return; }
                draftImages.Add(image); RenderDraftImages(); UpdateComposerState(); composer.Focus();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidOperationException || ex is UnityException)
            { ShowNotice("Не удалось добавить изображение. Выберите корректный PNG/JPEG до 8 МиБ; большая копия будет уменьшена. Для снимка нужна доступная камера и графический редактор. " + ex.Message, true); }
        }
        void RenderDraftImages()
        {
            if (imageAttachments == null) return;
            imageAttachments.Clear();
            foreach (var image in draftImages ?? new List<ChatAttachment>())
            {
                var selected = image; var card = BuildImageCard(image);
                card.Add(Button("×", () => { draftImages.Remove(selected); RenderDraftImages(); UpdateComposerState(); }, "image-remove"));
                imageAttachments.Add(card);
            }
            imageAttachments.style.display = draftImages != null && draftImages.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
        VisualElement BuildImageCard(ChatAttachment item)
        {
            var card = Element("image-card"); card.tooltip = item.Name ?? "Изображение";
            var thumbnail = Thumbnail(item);
            if (thumbnail) { var image = new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("image-thumbnail"); card.Add(image); }
            else card.Add(Text("Изображение", "image-missing"));
            card.Add(Text(item.Name ?? "Изображение", "image-name")); return card;
        }
        Texture2D Thumbnail(ChatAttachment item)
        {
            if (item == null || string.IsNullOrEmpty(item.Sha256)) return null;
            if (thumbnails.TryGetValue(item.Sha256, out var cached)) return cached;
            Texture2D source = null;
            try
            {
                byte[] bytes = Convert.FromBase64String(AgentViewCapture.ReadBase64(item));
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(source, bytes, false)) return null;
                int width = Math.Min(112, source.width), height = Math.Max(1, Mathf.RoundToInt(source.height * (width / (float)source.width)));
                if (height > 76) { width = Math.Max(1, Mathf.RoundToInt(width * (76f / height))); height = 76; }
                var pixels = source.GetPixels32(); var resized = new Color32[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) resized[y * width + x] = pixels[Math.Min(source.height - 1, y * source.height / height) * source.width + Math.Min(source.width - 1, x * source.width / width)];
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                texture.SetPixels32(resized); texture.Apply(false, true); thumbnails[item.Sha256] = texture; return texture;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is FormatException || ex is UnityException) { return null; }
            finally { if (source) DestroyImmediate(source); }
        }
        void ReleaseThumbnails() { foreach (var texture in thumbnails.Values) if (texture) DestroyImmediate(texture); thumbnails.Clear(); }
        void ClearAllAttachments() { ClearAttachment(); draftImages?.Clear(); RenderDraftImages(); }
        void OnDragUpdated(DragUpdatedEvent evt)
        { if (!controller.HasPendingWork && DragAndDrop.paths.Any(IsImagePath)) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; evt.StopPropagation(); } }
        void OnDragPerform(DragPerformEvent evt)
        {
            if (controller.HasPendingWork) return;
            var paths = DragAndDrop.paths.Where(IsImagePath).Take(3).ToArray(); if (paths.Length == 0) return;
            DragAndDrop.AcceptDrag(); foreach (string path in paths) { var source = path; AddImage(() => AgentViewCapture.Import(source)); } evt.StopPropagation();
        }
        static bool IsImagePath(string path) => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

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
                        imageDrafts[renderedSessionId] = new List<ChatAttachment>(draftImages ?? new List<ChatAttachment>());
                        draft = drafts.TryGetValue(session.Id, out var savedDraft) ? savedDraft : "";
                        composer.SetValueWithoutNotify(draft);
                        draftImages = imageDrafts.TryGetValue(session.Id, out var savedImages) ? new List<ChatAttachment>(savedImages) : new List<ChatAttachment>();
                        ClearAttachment();
                        skillsPanel?.RemoveFromHierarchy();
                        BuildSkillsPanel();
                    }
                    renderedSessionId = session.Id;
                    messageViews.Clear();
                    messageList.Clear();
                    ReleaseThumbnails();
                    RenderDraftImages();
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
                UpdateResponsiveLayout();
                titleLabel.text = controller.Current != null ? controller.Current.Title : "Новый чат";
                backgroundBlocked = ScriptDrafts.Pending || AgentSourcePatches.Pending || AgentEditorJobs.Busy;
                var models = controller.Models ?? new List<AvailableModel>();
                string signature = string.Join("\n", models.Select(m => m.Key + ":" + m.Name + ":" + m.Available + ":" + m.SupportsVision));
                if (signature != modelsSignature)
                {
                    modelsSignature = signature;
                    modelChoices.Clear();
                    modelChoices.AddRange(models);
                    var labels = modelChoices.Select(m => (string.IsNullOrEmpty(m.Name) ? m.Model : m.Name) + (m.SupportsVision ? " · зрение" : "") + (m.Available ? "" : " · недоступна")).ToList();
                    for (int i = 0; i < labels.Count; i++)
                        if (labels.Count(v => v == labels[i]) > 1) labels[i] += " · " + modelChoices[i].Key;
                    modelDropdown.choices = labels.Count > 0 ? labels : new List<string> { "Модели не найдены" };
                }
                int selected = modelChoices.FindIndex(m => m.Key == controller.Settings.ModelKey);
                modelDropdown.SetValueWithoutNotify(selected >= 0 ? modelDropdown.choices[selected] : "Выберите модель");
                modelDropdown.SetEnabled(!controller.HasPendingWork && modelChoices.Count > 0);
                if (addApiButton != null) addApiButton.SetEnabled(!controller.HasPendingWork && !backgroundBlocked && !EditorApplication.isCompiling);
                powerDropdown.SetValueWithoutNotify(PowerLabels[Mathf.Clamp((int)controller.Settings.Power, 0, 2)]);
                powerDropdown.SetEnabled(!controller.HasPendingWork);
                if (autoApply != null) { autoApply.SetValueWithoutNotify(controller.Settings.AutoApply); autoApply.SetEnabled(!controller.HasPendingWork); }
                foreach (var mode in modeButtons)
                { mode.Value.EnableInClassList("selected", mode.Key == controller.Settings.Mode); mode.Value.SetEnabled(!controller.HasPendingWork); }
                modeHint.text = controller.Settings.Mode == AssistantMode.Ask ? "Ответ и изучение проекта · без изменений" : controller.Settings.Mode == AssistantMode.Plan ? "Сначала покажу план · вы сможете применить его" : "Создам, проверю и покажу результат · код доступен для просмотра";
                if (skillsPanel != null) skillsPanel.Query<VisualElement>(className: "setting-control").ForEach(control => control.SetEnabled(!controller.HasPendingWork));
                EnsureSkillCatalog();
                skillsButton.text = "Навыки · " + skillCatalog.List().Count;
                connectionLabel.text = refreshingModels ? "Проверка подключения…" : controller.Connected ? "Подключено" : "Нет подключения";
                connectionLabel.EnableInClassList("connected", controller.Connected);
                refreshButton.SetEnabled(!controller.HasPendingWork && !refreshingModels);
                newChatButton.SetEnabled(!controller.HasPendingWork);
                undoButton.SetEnabled(!controller.HasPendingWork && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode && !backgroundBlocked);
                phaseLabel.text = !string.IsNullOrWhiteSpace(controller.Phase) ? controller.Phase : controller.HasPendingWork ? "Запрос выполняется" : controller.Connected ? "Готов к работе" : "Выберите доступную модель или нажмите «Обновить»";
                phaseLabel.EnableInClassList("working", controller.HasPendingWork);
            }
            finally { updatingControls = false; }
        }

        void RenderHistory()
        {
            var sessions = controller.Sessions ?? new List<ChatSession>();
            string signature = renderedSessionId + ":" + controller.HasPendingWork + ":" + string.Join("\n", sessions.Select(s => s.Id + ":" + s.Title + ":" + s.UpdatedUtc));
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
                button.SetEnabled(!controller.HasPendingWork);
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
            sendButton.text = controller.HasPendingWork ? "Остановить" : "Отправить";
            sendButton.EnableInClassList("stop-button", controller.HasPendingWork);
            bool available = modelChoices.Any(m => m.Key == controller.Settings.ModelKey && m.Available);
            bool vision = modelChoices.Any(m => m.Key == controller.Settings.ModelKey && m.SupportsVision);
            bool needsVision = draftImages != null && draftImages.Count > 0;
            imageHint.style.display = needsVision ? DisplayStyle.Flex : DisplayStyle.None;
            imageHint.text = vision ? "Изображений: " + draftImages.Count + " / 3 · модель увидит их вместе с запросом" : "Для изображений выберите модель с пометкой «зрение»";
            imageHint.EnableInClassList("warning", needsVision && !vision);
            sendButton.SetEnabled(controller.HasPendingWork || (controller.Connected && available && (!needsVision || vision) && !EditorApplication.isCompiling && !backgroundBlocked && !EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrWhiteSpace(composer.value)));
            sendButton.tooltip = controller.HasPendingWork ? "Остановить текущий запрос (Escape)" : needsVision && !vision ? "Выберите модель с поддержкой изображений или уберите вложения" : "Отправить сообщение (Enter)";
        }

        void OnComposerKey(KeyDownEvent evt)
        {
            if ((evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) || evt.shiftKey || IsComposing()) return;
#if !UNITY_6000_0_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopImmediatePropagation();
            if (!controller.HasPendingWork && sendButton.enabledSelf) SendDraft();
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
            if (compactLayout && historyDrawerOpen) SetSidebarVisible(false);
            if (skillsPanel != null) skillsPanel.style.display = DisplayStyle.None;
            if (!controller.HasPendingWork) return;
            controller.Cancel();
            Render();
#if !UNITY_6000_0_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopPropagation();
        }

        void OnSendOrStop()
        {
            if (controller.HasPendingWork) { controller.Cancel(); Render(); }
            else SendDraft();
        }

        void SendDraft()
        {
            string text = composer.value.Trim();
            if (text.Length == 0 || controller.HasPendingWork) return;
            string payload = string.IsNullOrEmpty(attachedSelection) ? text : text + "\n\nКонтекст выделения в Unity:\n" + attachedSelection;
            var images = (draftImages ?? new List<ChatAttachment>()).ToArray();
            if (images.Length > 0 && !modelChoices.Any(m => m.Key == controller.Settings.ModelKey && m.SupportsVision))
            { ShowNotice("Эта модель не поддерживает изображения. Выберите модель с пометкой «зрение» или уберите вложения.", false); return; }
            if (!EnsureEditMode()) return;
            if (payload.Length > 16000) { ShowNotice("Сообщение вместе с выделением превышает 16 000 символов. Сократите текст или уберите прикреплённый контекст.", true); return; }
            draft = "";
            composer.SetValueWithoutNotify("");
            ClearAllAttachments();
            CloseTaskMenu();
            notice.style.display = DisplayStyle.None;
            UpdateComposerState();
            Run(() => controller.SendAsync(payload, images));
            composer.Focus();
        }

        void SwitchChat(string id)
        {
            if (controller.HasPendingWork) return;
            if (compactLayout) SetSidebarVisible(false);
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
            if (controller.HasPendingWork || string.IsNullOrEmpty(text)) return;
            var messages = controller.Current.Messages;
            int index = messages.FindIndex(message => message.Id == messageId);
            var original = index > 0 ? messages.Take(index).LastOrDefault(message => message.Role == "user") : null;
            var images = original?.Attachments?.ToArray();
            notice.style.display = DisplayStyle.None;
            Run(() => controller.SendAsync(text, images));
        }

        void ResumeTask(string messageId)
        {
            if (!EnsureEditMode() || controller.HasPendingWork) return;
            Run(() => controller.ResumeAsync(messageId));
        }

        void RestoreTask(string messageId)
        {
            if (!EnsureEditMode() || controller.HasPendingWork) return;
            Invoke(() => controller.RestoreTask(messageId));
        }

        void ApplyPlan(string messageId)
        {
            if (!EnsureEditMode() || controller.HasPendingWork) return;
            Run(() => controller.ApplyPendingAsync(messageId));
        }

        void RevertCode(string messageId)
        {
            if (!EnsureEditMode() || controller.HasPendingWork) return;
            if (ScriptDrafts.Pending) { ShowNotice("Дождитесь текущей проверки кода, чтобы восстановить предыдущую версию.", false); return; }
            Invoke(() => controller.RevertCode(messageId));
        }

        bool EnsureEditMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !ScriptDrafts.Pending && !AgentSourcePatches.Pending && !AgentEditorJobs.Busy) return true;
            ShowNotice("Выйдите из режима игры и дождитесь завершения компиляции или фоновой проверки Unity.", false);
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
            readonly VisualElement steps, review, task, skillChips, images;
            readonly Button retry, apply, revertCode, resume, restore;
            AssistantMessage message;
            string stepsSignature, planSignature, textSignature, taskSignature, skillsSignature, imagesSignature;
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
                images = Element("message-images");
                Root.Add(images);
                body = Text("", "message-body");
                body.selection.isSelectable = true;
                Root.Add(body);
                status = Text("", "message-status");
                Root.Add(status);
                skillChips = Element("skill-chips"); Root.Add(skillChips);
                task = Element("task-card"); Root.Add(task);
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
                resume = Button("Продолжить и проверить", () => owner.ResumeTask(message.Id), "message-action");
                resume.tooltip = "Проверить фоновую задачу или продолжить незавершённую работу с её исходными требованиями";
                actions.Add(resume);
                restore = Button("Отменить этот запрос", () => owner.RestoreTask(message.Id), "message-action");
                restore.tooltip = "Вернуть сцену и файлы к состоянию до этого запроса. Последующие ручные изменения защищены проверкой конфликтов.";
                actions.Add(restore);
                Root.Add(actions);
            }

            public bool Update(AssistantMessage value)
            {
                message = value;
                string content = (value.Text ?? "") + "\n" + value.Status + "\n" + value.Model + "\n" + value.ElapsedMs + ":" + value.FirstTokenMs + ":" + value.IsError;
                bool changed = content != textSignature;
                textSignature = content;
                string empty = owner.controller.HasPendingWork ? "Изучаю запрос…" :
                    value.Status != null && value.Status.IndexOf("отмен", StringComparison.OrdinalIgnoreCase) >= 0 ? "Запрос отменён." : "Ответ ещё не получен.";
                body.text = string.IsNullOrEmpty(value.Text) ? empty : value.Text;
                status.text = HumanStatus(value.Status);
                status.style.display = string.IsNullOrEmpty(value.Status) ? DisplayStyle.None : DisplayStyle.Flex;
                Root.EnableInClassList("error-message", value.IsError);
                metadata.text = value.Role == "user" ? "" : string.Join(" · ", new[] { value.Model, value.ElapsedMs > 0 ? (value.ElapsedMs / 1000d).ToString("0.0") + " с" : null }.Where(s => !string.IsNullOrEmpty(s)));
                metadata.tooltip = value.FirstTokenMs > 0 ? "Первый фрагмент ответа: " + (value.FirstTokenMs / 1000d).ToString("0.0") + " с" : "";
                retry.SetEnabled(!owner.controller.HasPendingWork && !owner.backgroundBlocked && !string.IsNullOrEmpty(owner.OriginalRequest(value.Id)));
                apply.style.display = value.PendingPlan == null ? DisplayStyle.None : DisplayStyle.Flex;
                apply.SetEnabled(!owner.controller.HasPendingWork && !owner.backgroundBlocked && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode);
                bool canRevert = value.Role == "assistant" && value.Steps != null &&
                    value.Steps.Any(step => step.Tool == "draft_script" && step.Success) &&
                    ScriptDrafts.CanRevert(owner.controller.Current.Id, value.Id);
                if (revertVisible != canRevert) { revertVisible = canRevert; changed = true; }
                revertCode.style.display = canRevert ? DisplayStyle.Flex : DisplayStyle.None;
                revertCode.SetEnabled(canRevert && !owner.controller.HasPendingWork && !EditorApplication.isCompiling &&
                    !EditorApplication.isPlayingOrWillChangePlaymode && !owner.backgroundBlocked);
                bool canResume = value.Role == "assistant" && value.Task != null && value.PendingPlan == null &&
                    (!string.IsNullOrEmpty(value.Task.PendingJobId) || value.Task.ContinueAfterReload || value.Task.MutationAuthorized && (value.IsError || value.Task.Checks?.Any(check => !check.Passed) == true));
                resume.style.display = canResume ? DisplayStyle.Flex : DisplayStyle.None;
                resume.SetEnabled(canResume && !owner.controller.HasPendingWork && !owner.backgroundBlocked && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode);
                bool hasRestore = value.Role == "assistant" && !string.IsNullOrEmpty(value.Task?.CheckpointId) && value.Task.Status != "restored";
                restore.style.display = hasRestore ? DisplayStyle.Flex : DisplayStyle.None;
                // CanRestore may inspect/save a scene copy: never call it from this rendering loop.
                restore.SetEnabled(hasRestore && !owner.controller.HasPendingWork && !owner.backgroundBlocked && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode && string.IsNullOrEmpty(value.Task.PendingJobId));
                string taskState = value.Task == null ? "" : JsonConvert.SerializeObject(value.Task);
                if (taskState != taskSignature) { taskSignature = taskState; RenderTask(value.Task); changed = true; }
                string skillState = string.Join("\n", value.Skills ?? new List<string>());
                if (skillState != skillsSignature) { skillsSignature = skillState; RenderSkills(value.Skills); changed = true; }
                string imageState = string.Join("\n", (value.Attachments ?? new List<ChatAttachment>()).Select(item => item.Sha256 + ":" + item.Name));
                if (imageState != imagesSignature) { imagesSignature = imageState; RenderImages(value.Attachments); changed = true; }
                string signature = string.Join("\n", (value.Steps ?? new List<EditorCommandResult>()).Select(s => s.Success + ":" + s.Summary + ":" + s.Target + ":" + s.Error));
                if (signature != stepsSignature)
                {
                    stepsSignature = signature;
                    RenderSteps(value.Steps);
                    changed = true;
                }
                var displayedPlan = value.PendingPlan ?? HistoricalCodePlan(value);
                bool awaitingReview = value.PendingPlan != null;
                string pending = displayedPlan == null ? "" : (awaitingReview ? "pending:" : "history:") + displayedPlan.reply + "\n" + string.Join("\n", displayedPlan.actions.Select(a => a.tool + ":" + a.name + ":" + a.target + ":" + a.args));
                if (pending != planSignature)
                {
                    planSignature = pending;
                    RenderPlan(displayedPlan, awaitingReview);
                    changed = true;
                }
                return changed;
            }

            void RenderImages(List<ChatAttachment> values)
            {
                images.Clear(); images.style.display = values != null && values.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var value in (values ?? new List<ChatAttachment>()).Take(4)) images.Add(owner.BuildImageCard(value));
            }

            void RenderSkills(List<string> values)
            {
                skillChips.Clear(); skillChips.style.display = values != null && values.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (values == null || values.Count == 0) return;
                skillChips.Add(Text("Знания:", "skill-chip-caption"));
                foreach (string value in values.Take(5)) { string id = value; var chip = Button(SkillTitle(id), () => owner.OpenSkills(id), "skill-chip"); chip.tooltip = "Прочитать использованный навык"; skillChips.Add(chip); }
            }

            void RenderTask(AgentTaskState value)
            {
                bool show = value != null && (value.MutationAuthorized || value.Requirements?.Count > 0 || value.Checks?.Count > 0 || !string.IsNullOrEmpty(value.PendingJobId));
                task.Clear(); task.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) return;
                var checks = value.Checks ?? new List<AgentCheckResult>();
                var requirements = value.Requirements ?? new List<AgentRequirement>();
                int passed = checks.Count(check => check.Passed);
                string title = checks.Count > 0 ? "Проверено " + passed + " из " + checks.Count : value.PendingJobId != null && value.PendingJobId.Length > 0 ? "Проверка ещё выполняется" : "Готовность задачи";
                task.Add(Text(title, "task-title"));
                task.Add(Text(HumanStatus(value.Status), "task-state"));
                if (checks.Count > 0)
                {
                    foreach (var check in checks.Take(20))
                    {
                        var requirement = requirements.FirstOrDefault(item => item.Id == check.RequirementId);
                        string expected = requirement == null ? "" : " · нужно " + requirement.Number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                        string actual = string.IsNullOrWhiteSpace(check.Actual) ? "" : " · получено " + check.Actual;
                        task.Add(Text((check.Passed ? "✓ " : "! ") + HumanCriterion(check.Description) + expected + actual, check.Passed ? "task-check-pass" : "task-check-fail"));
                    }
                }
                else if (requirements.Count > 0)
                    foreach (var requirement in requirements.Take(20)) task.Add(Text("○ " + HumanCriterion(requirement.Description) + " · ещё не проверено", "task-check-pending"));
                else task.Add(Text(message.PendingPlan != null ? "Изменения ещё не выполнены. После применения появятся результаты проверок." : "Отдельные критерии не определены. Результаты выполненных действий доступны ниже.", "task-check-pending"));
                foreach (string question in (value.OpenQuestions ?? new List<string>()).Take(6)) task.Add(Text("Уточнение: " + question, "task-check-pending"));
                var proof = new Foldout { text = "Как проверено", value = false };
                foreach (var check in checks.Where(item => !string.IsNullOrWhiteSpace(item.Evidence)).Take(20)) proof.Add(Text(HumanCriterion(check.Description) + ": " + check.Evidence, "task-evidence"));
                foreach (string entry in (value.Evidence ?? new List<string>()).Take(12)) proof.Add(Text(entry.Length > 1200 ? entry.Substring(0, 1200) + "…" : entry, "task-evidence"));
                if (proof.childCount > 0) task.Add(proof);
            }

            static string HumanCriterion(string value)
            {
                string text = value ?? "Проверка";
                foreach (var pair in new[] { new[] { "point_light", "точечных источников света" }, new[] { "tree", "деревьев" }, new[] { "house", "домов" }, new[] { "rock", "камней" }, new[] { "light", "источников света" }, new[] { "npc", "персонажей" } })
                    text = System.Text.RegularExpressions.Regex.Replace(text, @"\b" + pair[0] + @"\b", pair[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return text;
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
                    else if (!string.IsNullOrWhiteSpace(summary))
                    {
                        var data = new Foldout { text = "Подробные данные", value = false };
                        data.Add(PreviewField(summary.Length > 8000 ? summary.Substring(0, 8000) + "\n… данные сокращены" : summary, "technical-preview"));
                        row.Add(data);
                    }
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

            static EditorPlan HistoricalCodePlan(AssistantMessage value)
            {
                if (value.Attempts == null || value.Steps == null ||
                    !value.Steps.Any(step => (step.Tool == "draft_script" || step.Tool == "patch_script") && step.Success)) return null;
                foreach (var attempt in value.Attempts.Where(attempt => attempt != null && attempt.Accepted).Reverse())
                {
                    string source = string.IsNullOrEmpty(attempt.NormalizedResponse) ? attempt.Response : attempt.NormalizedResponse;
                    if (string.IsNullOrEmpty(source) || source.Length > 40000 || !source.Contains("\"draft_script\"") && !source.Contains("\"patch_script\"")) continue;
                    try
                    {
                        var parsed = EditorPlan.Parse(source);
                        var drafts = parsed.actions.Where(action => action.tool == "draft_script" || action.tool == "patch_script").ToList();
                        if (drafts.Count > 0) return new EditorPlan { reply = parsed.reply, complete = parsed.complete, actions = drafts };
                    }
                    catch (ArgumentException) { }
                    catch (Newtonsoft.Json.JsonException) { }
                }
                return null;
            }

            void RenderPlan(EditorPlan plan, bool awaitingReview)
            {
                review.Clear();
                review.style.display = plan == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (plan == null) return;
                review.Add(Text(awaitingReview ? "План ждёт подтверждения" : "Код из истории сообщения", "review-title"));
                if (awaitingReview) review.Add(Text("Проверьте объекты и параметры. Эти изменения ещё не выполнены.", "review-caption"));
                foreach (var action in plan.actions)
                {
                    if (action.tool == "draft_script")
                    {
                        string name = string.IsNullOrWhiteSpace(action.name) ? "НовыйСкрипт" : action.name;
                        if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) name += ".cs";
                        var foldout = new Foldout { text = "Код · " + name, value = awaitingReview };
                        string description = action.args?["description"]?.ToString();
                        if (!string.IsNullOrEmpty(description)) foldout.Add(Text(description, "review-description"));
                        string code = action.args?["code"]?.ToString() ?? "";
                        var field = new TextField { multiline = true, isReadOnly = true, value = code, selectAllOnFocus = false, selectAllOnMouseUp = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
                        field.AddToClassList("code-preview");
                        foldout.Add(field);
                        foldout.Add(Button("Копировать код", () => EditorGUIUtility.systemCopyBuffer = code, "message-action"));
                        review.Add(foldout);
                        review.Add(Text(awaitingReview ? "Скрипт попадёт в проект после «Применить план». Результат импорта и компиляции появится в чате." :
                            "Сохранённый снимок кода из этого ответа. Текущее состояние файла может отличаться.", "review-caption"));
                    }
                    else if (action.tool == "patch_script")
                    {
                        review.Add(Text("Изменить C# · " + (action.args?["path"]?.ToString() ?? action.name), "review-action"));
                        string purpose = action.args?["description"]?.ToString();
                        if (!string.IsNullOrEmpty(purpose)) review.Add(Text(purpose, "review-description"));
                        var before = new Foldout { text = "Заменяемый фрагмент", value = awaitingReview };
                        before.Add(PreviewField(action.args?["find"]?.ToString() ?? "", "code-preview")); review.Add(before);
                        var after = new Foldout { text = "Новый фрагмент", value = awaitingReview };
                        after.Add(PreviewField(action.args?["replace"]?.ToString() ?? "", "code-preview")); review.Add(after);
                        review.Add(Text(awaitingReview ? "Изменится только этот фрагмент. Перед записью проверяется версия файла; затем Unity проверит компиляцию." : "Снимок изменения из этого ответа. Текущий файл мог измениться позже.", "review-caption"));
                    }
                    else
                    {
                        string title = HumanTool(action.tool);
                        if (!string.IsNullOrEmpty(action.name)) title += " · " + action.name;
                        if (!string.IsNullOrEmpty(action.target)) title += " · «" + action.target + "»";
                        var item = Element("plan-item");
                        item.Add(Text(title, "review-action"));
                        string before = BeforeAction(action);
                        if (!string.IsNullOrEmpty(before)) item.Add(Text("Сейчас: " + before, "plan-before"));
                        string after = DescribeArguments(action.args);
                        if (!string.IsNullOrEmpty(after)) item.Add(Text("План: " + after, "plan-after"));
                        if (action.args != null && action.args.Count > 0)
                        {
                            string json = action.args.ToString(Formatting.Indented);
                            var details = new Foldout { text = "Технические параметры", value = false };
                            details.Add(PreviewField(json.Length > 5000 ? json.Substring(0, 5000) + "\n… параметры сокращены" : json, "technical-preview"));
                            item.Add(details);
                        }
                        review.Add(item);
                    }
                }
            }

            static TextField PreviewField(string text, string className)
            {
                var field = new TextField { multiline = true, isReadOnly = true, value = text, selectAllOnFocus = false, selectAllOnMouseUp = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
                field.AddToClassList(className); return field;
            }

            static string BeforeAction(EditorCommand action)
            {
                if (string.IsNullOrWhiteSpace(action.target)) return null;
                try
                {
                    var target = SceneToolExecutor.ResolveTarget(action.target);
                    if (!target) return "цель пока не найдена в сцене";
                    if (action.tool == "set_transform") return "позиция " + target.transform.position.ToString("0.##") + ", поворот " + target.transform.eulerAngles.ToString("0.##") + ", масштаб " + target.transform.localScale.ToString("0.##");
                    if (action.tool == "delete_object") return "«" + target.name + "», дочерних объектов: " + Math.Max(0, target.GetComponentsInChildren<Transform>(true).Length - 1);
                    if (action.tool == "set_lighting")
                    {
                        var light = target.GetComponent<Light>();
                        if (light) return "«" + target.name + "», интенсивность " + light.intensity.ToString("0.##") + ", радиус " + light.range.ToString("0.##") + " м";
                    }
                    return "«" + target.name + "» в текущей сцене";
                }
                catch (ArgumentException) { return "цель будет проверена при применении"; }
            }

            static string DescribeArguments(JObject args)
            {
                if (args == null) return "";
                var values = new List<string>();
                foreach (var property in args.Properties().Where(item => item.Name != "code").Take(10))
                {
                    string label;
                    switch (property.Name)
                    {
                        case "width": label = "ширина, м"; break; case "length": label = "длина, м"; break;
                        case "height": label = "высота, м"; break; case "trees": label = "деревьев"; break;
                        case "buildings": case "houses": label = "домов"; break; case "rocks": label = "камней"; break;
                        case "position": label = "позиция"; break; case "rotation": label = "поворот"; break;
                        case "scale": label = "масштаб"; break; case "count": label = "количество"; break;
                        case "style": label = "стиль"; break; case "role": label = "роль"; break;
                        case "personality": label = "характер"; break; case "goals": label = "цели"; break;
                        case "knowledge": label = "знания"; break; case "behavior": label = "поведение"; break;
                        case "speed": label = "скорость"; break; case "intensity": label = "интенсивность"; break;
                        case "range": label = "радиус, м"; break; case "path": label = "файл"; break;
                        case "on_terrain": label = "учесть поверхность"; break; case "description": label = "назначение"; break;
                        case "player": label = "управляемый игрок"; break; case "quest": label = "квест"; break;
                        case "places": label = "точки маршрута"; break; case "reward": label = "награда"; break;
                        case "giver": label = "кто выдаёт квест"; break; case "recipient": label = "кому доставить"; break;
                        case "type": label = "тип"; break; case "property": label = "настройка"; break; case "value": label = "значение"; break;
                        default: label = property.Name.Replace('_', ' '); break;
                    }
                    string value = property.Value.Type == JTokenType.Boolean ? ((bool)property.Value ? "да" : "нет") : property.Value is JArray array ? string.Join(", ", array.Take(6).Select(item => item.ToString(Formatting.None))) + (array.Count > 6 ? "…" : "") : property.Value.ToString(Formatting.None).Trim('"');
                    if (value.Length > 160) value = value.Substring(0, 160) + "…";
                    values.Add(label + ": " + value);
                }
                return string.Join(" · ", values);
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
                    case "planned": return "План подготовлен";
                    case "running": case "executing": return "Выполняю задачу";
                    case "verifying": case "checking": return "Проверяю результат";
                    case "waiting_compile": case "waiting_compilation": case "compiling": return "Unity проверяет компиляцию";
                    case "waiting_job": case "waiting_editor_job": case "job_pending": return "Ожидаю проверку Unity";
                    case "waiting_continuation": return "Продолжение";
                    case "interrupted": return "Работу можно продолжить";
                    case "literal_checks_passed": return "Критерии запроса подтверждены по сцене";
                    case "checks_passed": return "Проверки запроса пройдены";
                    case "checks_failed": return "Часть критериев пока не выполнена";
                    case "actions_need_review": return "Нужна отдельная проверка результата";
                    case "restored": return "Изменения этого запроса отменены";
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
                    case "compile_scripts": return "Проверить компиляцию C#";
                    case "compile_patch": return "Проверить компиляцию изменённого C#";
                    case "run_play_check": return "Измерить поведение в Play Mode";
                    case "patch_script": return "Изменить существующий C#";
                    case "inspect_project": return "Изучить проект";
                    case "find_scene_objects": return "Найти объекты сцены";
                    case "inspect_component": return "Изучить компонент";
                    case "set_component": return "Настроить компонент";
                    case "read_console": return "Прочитать ошибки Unity";
                    case "capture_scene": return "Получить изображение сцены";
                    case "list_skills": return "Подобрать навыки";
                    case "read_skill": return "Прочитать подход к задаче";
                    case "open_scene": return "Открыть сцену";
                    case "create_village": return "Создать игровую деревню";
                    case "bake_navigation": return "Подготовить навигацию";
                    case "set_navigation": return "Настроить маршрут NPC";
                    case "create_player": return "Добавить управляемого игрока";
                    case "create_delivery_quest": return "Создать квест доставки";
                    case "catalog_assets": return "Подобрать ресурсы проекта";
                    case "validate_world": return "Проверить мир и маршруты";
                    case "project_context": return "Изучить контекст проекта";
                    case "find_objects": return "Найти объекты";
                    case "instantiate_prefab": return "Добавить префаб в сцену";
                    case "modify_component": return "Настроить компонент";
                    case "set_serialized_property": return "Изменить настройку компонента";
                    case "run_tests": return "Запустить тесты";
                    case "poll_job": return "Проверить результат фоновой задачи";
                    case "build_player": return "Собрать игру";
                    case "set_build_scenes": return "Сцены сборки";
                    case "capture_view": return "Проверить изображение сцены";
                    case "validate_scene": return "Проверить сцену";
                    case "bake_navmesh": return "Подготовить навигацию";
                    case "verify_path": return "Проверить маршрут";
                    case "remember_design": return "Сохранить решение проекта";
                    case "recall_design": return "Вспомнить решения проекта";
                    default: return "Действие с объектом";
                }
            }
        }
    }
}
