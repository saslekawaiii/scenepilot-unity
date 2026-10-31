using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Reviewed generated C# imports, real compiler feedback and a restorable file backup.</summary>
    [InitializeOnLoad]
    public static class ScriptDrafts
    {
        public static event Action Changed;
        private const string Folder = "Assets/WorldWeaverGenerated/Scripts";
        private const string Marker = "// Generated with WorldWeaver. User-reviewed C# source.";
        private const string StatePath = "UserSettings/WorldWeaverChatCode.json";
        private const string JournalFolder = "UserSettings/WorldWeaverChatCode";
        private static readonly List<string> errors = new List<string>();

        [Serializable] private sealed class FileBackup { public string Path, Before, After, BeforeBytes, BeforeSha256, AfterSha256; public bool Existed; }
        [Serializable] private sealed class State
        {
            public string ImportId, SessionId, MessageId, HistoryFolder;
            public long CreatedUtcTicks;
            public bool Pending, Reverted, Restoring, CompilationConfirmed, CompilationSucceeded;
            public string Outcome = "";
            public List<FileBackup> Files = new List<FileBackup>();
        }

        public static string ToolInstructions => @"CODE TOOLS (always reviewed before import):
draft_script: name=ASCII C# MonoBehaviour class name (no extension/path), target='', args={code:'complete C# source',description:'Russian purpose'}. Code must declare that exact class deriving MonoBehaviour, use Unity APIs, have no hard-coded credentials, and avoid Editor-only APIs. Create small self-contained scripts; no shell, package downloads or changes to project settings. Existing own generated script of the same class can be revised; all other files are preserved. Do not attach a draft until it compiles. The user can apply/reject the code in chat. Real Unity compilation results are included in context; repair errors with a new draft of the same name.
attach_script: name=class name of an ALREADY COMPILED WorldWeaver-generated script, target=existing scene object, args={}. This is also a reviewed change. Never attempt attach_script in the same plan as draft_script. Use attach_behavior for existing built-in rotation/patrol/bob without drafting code.";

        static ScriptDrafts()
        {
            CompilationPipeline.compilationStarted += _ => errors.Clear();
            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                if (!Pending) return;
                foreach (var message in messages.Where(m => m.type == CompilerMessageType.Error))
                    if (errors.Count < 20) errors.Add(message.file + ":" + message.line + " " + message.message);
            };
            CompilationPipeline.compilationFinished += _ => Finish();
            // Recover only an import that was already pending when this domain loaded.
            // A newly reviewed import schedules its own compiler request below.
            if (Pending) EditorApplication.delayCall += RecoverAfterReload;
        }

        private static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
        private static byte[] OriginalBytes(FileBackup file) => file.BeforeBytes != null ? Convert.FromBase64String(file.BeforeBytes) : new UTF8Encoding(false).GetBytes(file.Before ?? "");
        private static State Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var state = JsonConvert.DeserializeObject<State>(File.ReadAllText(path));
                if (state == null || state.Files == null || state.Files.Count == 0 || state.Files.Count > 4 ||
                    !ChatHistoryStore.ValidId(state.SessionId) || !ChatHistoryStore.ValidId(state.MessageId) ||
                    state.Files.Any(f => f == null || f.Path == null || !Regex.IsMatch(f.Path, "^" + Regex.Escape(Folder) + @"/[A-Za-z_][A-Za-z0-9_]{0,79}\.cs$") || f.After == null || !f.After.StartsWith(Marker, StringComparison.Ordinal))) return null;
                // Legacy single-backup state is migrated before the next import replaces its mirror.
                if (string.IsNullOrEmpty(state.ImportId)) state.ImportId = Hash(Encoding.UTF8.GetBytes(state.SessionId + "|" + state.MessageId + "|" + string.Join("|", state.Files.Select(f => f.Path + f.After)))).Substring(0, 32);
                if (!Regex.IsMatch(state.ImportId, "^[a-f0-9]{32}$")) return null;
                if (state.CreatedUtcTicks == 0) state.CreatedUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
                foreach (var file in state.Files)
                {
                    if (file.AfterSha256 == null) file.AfterSha256 = Hash(new UTF8Encoding(false).GetBytes(file.After));
                    if (file.Existed && file.BeforeSha256 == null) file.BeforeSha256 = Hash(OriginalBytes(file));
                }
                if (!state.Pending && !state.Reverted && state.Outcome == "Unity завершила компиляцию без ошибок.")
                { state.CompilationConfirmed = true; state.CompilationSucceeded = true; }
                return state;
            }
            catch (IOException) { return null; }
            catch (JsonException) { return null; }
            catch (FormatException) { return null; }
        }
        private static State Load() => States().OrderByDescending(s => s.CreatedUtcTicks).FirstOrDefault();
        private static List<State> States()
        {
            var states = Directory.Exists(JournalFolder) ? Directory.GetFiles(JournalFolder, "*.json").Select(Read).Where(s => s != null).ToList() : new List<State>();
            var mirror = Read(StatePath);
            if (mirror != null && !states.Any(s => s.ImportId == mirror.ImportId)) states.Add(mirror);
            return states;
        }
        private static void AtomicBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void Save(State state)
        {
            var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(state, Formatting.Indented));
            AtomicBytes(JournalFolder + "/" + state.ImportId + ".json", bytes);
            // Compatibility mirror for tooling; message-specific operations always use the journal.
            AtomicBytes(StatePath, bytes);
        }
        private static void ArchiveMirror()
        {
            var previous = Read(StatePath);
            if (previous != null && !File.Exists(JournalFolder + "/" + previous.ImportId + ".json")) Save(previous);
        }
        private static bool Matches(State state, bool restored = false) => state.Files.All(file =>
            restored ? file.Existed ? FileHash(file.Path) == file.BeforeSha256 : !File.Exists(file.Path) : FileHash(file.Path) == file.AfterSha256);
        private static State Find(string sessionId, string messageId) => States().Where(s => s.SessionId == sessionId &&
            (messageId == null || s.MessageId == messageId) && !s.Reverted).OrderByDescending(s => s.CreatedUtcTicks).FirstOrDefault();
        public static bool Pending => States().Any(s => s.Pending);
        public static bool CanRevert(string sessionId, string messageId = null)
        {
            if (Pending) return false;
            var state = Find(sessionId, messageId);
            return state != null && Matches(state);
        }
        private static bool CurrentSourceWasCompiled(string path)
        {
            string current = FileHash(path);
            return current != null && States().Any(state => !state.Pending && state.CompilationConfirmed && state.CompilationSucceeded &&
                state.Files.Any(file => file.Path == path && current == (state.Restoring ? file.BeforeSha256 : file.AfterSha256)));
        }
        public static string CompilationContext()
        {
            var states = States().OrderByDescending(s => s.CreatedUtcTicks).ToList();
            var latest = states.FirstOrDefault();
            return latest == null ? "No reviewed C# imports yet." : JsonConvert.SerializeObject(new { Pending, latest.Reverted, latest.Outcome,
                files = latest.Files.Select(f => f.Path), imports = states.Take(12).Select(s => new { s.MessageId, s.Pending, s.Reverted, s.CompilationConfirmed, s.CompilationSucceeded, s.Outcome }) });
        }

        public static void Validate(EditorCommand action)
        {
            if (action.tool != "draft_script" && action.tool != "attach_script") throw new ArgumentException("Unknown code tool.");
            if (!Regex.IsMatch(action.name ?? "", "^[A-Za-z_][A-Za-z0-9_]{0,79}$")) throw new ArgumentException("Имя C# класса: латинские буквы, цифры и подчёркивание, без пути.");
            if (action.tool == "attach_script")
            {
                if (action.args.Count != 0) throw new ArgumentException("attach_script не принимает args.");
                var path = Folder + "/" + action.name + ".cs";
                if (!File.Exists(path) || !File.ReadAllText(path).StartsWith(Marker, StringComparison.Ordinal)) throw new ArgumentException("Можно подключать только ранее проверенный скрипт WorldWeaver.");
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = script ? script.GetClass() : null;
                if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract || !CurrentSourceWasCompiled(path))
                    throw new ArgumentException("Подключение требует подтверждённой успешной компиляции текущего файла. Исправьте код и повторите просмотренный импорт; старый тип из прежней сборки не используется.");
                SceneToolExecutor.ResolveTarget(action.target);
                return;
            }
            if (action.args.Properties().Any(p => p.Name != "code" && p.Name != "description")) throw new ArgumentException("draft_script args: code и description.");
            var codeToken = action.args["code"];
            if (codeToken?.Type != Newtonsoft.Json.Linq.JTokenType.String) throw new ArgumentException("draft_script.code должен быть строкой C#.");
            var code = (string)codeToken;
            if (string.IsNullOrWhiteSpace(code) || code.Length > 35000 || !Regex.IsMatch(code, @"\bclass\s+" + Regex.Escape(action.name) + @"\s*:\s*(?:UnityEngine\.)?MonoBehaviour\b"))
                throw new ArgumentException("Код должен содержать полный MonoBehaviour с именем " + action.name + " (до 35 000 символов).");
            if (code.Contains("```") || code.Contains("[InitializeOnLoad") || code.Contains("UnityEditor") || ChatHistoryStore.Redact(code) != code)
                throw new ArgumentException("Предоставьте обычный runtime C# без Markdown, Editor initialization и секретных ключей.");
            string destination = Folder + "/" + action.name + ".cs";
            if (File.Exists(destination) && !File.ReadAllText(destination).StartsWith(Marker, StringComparison.Ordinal))
                throw new ArgumentException("Существующий пользовательский скрипт не будет перезаписан. Выберите новое имя класса.");
        }

        public static List<EditorCommandResult> Apply(List<EditorCommand> actions, string sessionId, string messageId, string historyFolder)
        {
            if (actions.Count == 0 || actions.Count > 4 || actions.Select(a => a.name).Distinct(StringComparer.Ordinal).Count() != actions.Count)
                throw new ArgumentException("За один просмотр можно импортировать 1–4 скрипта с разными именами.");
            foreach (var action in actions) Validate(action);
            if (Pending) throw new InvalidOperationException("Дождитесь текущей проверки кода.");
            ArchiveMirror();
            byte[] previousMirror = File.Exists(StatePath) ? File.ReadAllBytes(StatePath) : null;
            var state = new State { ImportId = Guid.NewGuid().ToString("N"), CreatedUtcTicks = DateTime.UtcNow.Ticks,
                SessionId = sessionId, MessageId = messageId, HistoryFolder = historyFolder, Pending = true };
            Directory.CreateDirectory(Folder);
            foreach (var action in actions)
            {
                string path = Folder + "/" + action.name + ".cs"; bool exists = File.Exists(path);
                byte[] before = exists ? File.ReadAllBytes(path) : null;
                string after = Marker + "\n" + (string)action.args["code"] + "\n";
                state.Files.Add(new FileBackup { Path = path, Existed = exists, Before = exists ? File.ReadAllText(path) : null,
                    BeforeBytes = exists ? Convert.ToBase64String(before) : null, BeforeSha256 = exists ? Hash(before) : null,
                    After = after, AfterSha256 = Hash(new UTF8Encoding(false).GetBytes(after)) });
            }
            var attempted = new List<FileBackup>();
            try
            {
                // Persist every before-image before writing any source file.
                Save(state);
                foreach (var file in state.Files)
                { attempted.Add(file); AtomicBytes(file.Path, new UTF8Encoding(false).GetBytes(file.After)); }
                if (!Matches(state)) throw new IOException("Не удалось проверить записанные исходники.");
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (var file in attempted.AsEnumerable().Reverse()) try
                { if (file.Existed) AtomicBytes(file.Path, OriginalBytes(file)); else if (File.Exists(file.Path)) File.Delete(file.Path); }
                catch (Exception rollback) { failures.Add(file.Path + ": " + rollback.Message); }
                if (failures.Count == 0)
                {
                    string journal = JournalFolder + "/" + state.ImportId + ".json";
                    if (File.Exists(journal)) File.Delete(journal);
                    if (previousMirror != null) AtomicBytes(StatePath, previousMirror); else if (File.Exists(StatePath)) File.Delete(StatePath);
                    throw;
                }
                state.Pending = false; state.Outcome = "Импорт не завершён; восстановление требует проверки: " + string.Join("; ", failures);
                Save(state); throw new IOException(state.Outcome, error);
            }
            errors.Clear();
            EditorApplication.delayCall += () => { AssetDatabase.Refresh(); CompilationPipeline.RequestScriptCompilation(); };
            return state.Files.Select(file => new EditorCommandResult { Success = true, Tool = "draft_script", Target = file.Path,
                Summary = "C# сохранён после просмотра; итог компиляции появится в чате.", CreatedAssets = new List<string> { file.Path } }).ToList();
        }

        public static EditorCommandResult Attach(EditorCommand action)
        {
            Validate(action);
            var target = SceneToolExecutor.ResolveTarget(action.target);
            var type = AssetDatabase.LoadAssetAtPath<MonoScript>(Folder + "/" + action.name + ".cs").GetClass();
            if (target.GetComponent(type)) return new EditorCommandResult { Success = true, Tool = action.tool, Target = target.name, Summary = "Компонент " + action.name + " уже подключён." };
            var component = Undo.AddComponent(target, type);
            if (!component) throw new InvalidOperationException("Unity не добавила компонент.");
            return new EditorCommandResult { Success = true, Tool = action.tool, Target = target.name, Summary = "Подключён скомпилированный " + action.name + "." };
        }

        private static void Finish()
        {
            var state = States().Where(s => s.Pending).OrderByDescending(s => s.CreatedUtcTicks).FirstOrDefault();
            if (state == null) return;
            state.Pending = false; state.CompilationConfirmed = true;
            state.CompilationSucceeded = errors.Count == 0 && Matches(state, state.Restoring);
            state.Outcome = errors.Count > 0 ? "Ошибки компиляции:\n" + string.Join("\n", errors) : !state.CompilationSucceeded
                ? "Исходник изменился во время компиляции; текущая версия не подтверждена."
                : state.Restoring ? "Файлы восстановлены, Unity завершила повторную компиляцию без ошибок." : "Unity завершила компиляцию без ошибок.";
            Save(state); UpdateHistory(state, state.CompilationSucceeded); Changed?.Invoke();
        }
        private static void RecoverAfterReload()
        {
            var state = States().Where(s => s.Pending).OrderByDescending(s => s.CreatedUtcTicks).FirstOrDefault();
            if (state == null || EditorApplication.isCompiling) return;
            // GetClass may still expose a previous assembly after a newer source failed.
            state.Pending = false; state.CompilationConfirmed = false; state.CompilationSucceeded = false;
            state.Outcome = "Результат компиляции после перезапуска Unity не подтверждён. Проверьте Console и повторите импорт исправленного кода.";
            Save(state); UpdateHistory(state, false); Changed?.Invoke();
        }
        private static void UpdateHistory(State state, bool success)
        {
            var store = new ChatHistoryStore(state.HistoryFolder);
            var session = store.Load().FirstOrDefault(s => s.Id == state.SessionId);
            var message = session?.Messages.FirstOrDefault(m => m.Id == state.MessageId);
            if (message == null) return;
            message.IsError = !success; message.Status = success ? state.Restoring ? "Код восстановлен" : "Код скомпилирован" : "Нужно проверить код";
            message.Steps.Add(new EditorCommandResult { Success = success, Tool = state.Restoring ? "restore_compile_scripts" : "compile_scripts",
                Target = string.Join(", ", state.Files.Select(f => f.Path)), Summary = state.Outcome, Error = success ? null : state.Outcome });
            store.Save(session);
        }

        public static void Revert(string sessionId, string messageId = null)
        {
            if (Pending) throw new InvalidOperationException("Дождитесь компиляции текущего импорта.");
            var state = Find(sessionId, messageId);
            if (state == null) return;
            // Do not overwrite edits made by the developer after the agent's import.
            if (!Matches(state))
                throw new InvalidOperationException("Файл изменён вручную после импорта. Автоматическая отмена сохранит ваши правки; восстановите нужную версию через Git.");
            var attempted = new List<FileBackup>();
            var metadata = state.Files.ToDictionary(f => f.Path, f => File.Exists(f.Path + ".meta") ? File.ReadAllBytes(f.Path + ".meta") : null);
            try
            {
                foreach (var file in state.Files)
                {
                    // Include a write that fails halfway so its original import is restored too.
                    attempted.Add(file);
                    if (file.Existed)
                    {
                        AtomicBytes(file.Path, OriginalBytes(file));
                        if (FileHash(file.Path) != file.BeforeSha256)
                            throw new IOException("Не удалось проверить восстановление " + file.Path + ".");
                    }
                    else if (!AssetDatabase.DeleteAsset(file.Path) || File.Exists(file.Path))
                        throw new IOException("Unity не удалила " + file.Path + ".");
                }
                state.Reverted = true; state.Restoring = true; state.Pending = true; state.CompilationConfirmed = false; state.CompilationSucceeded = false;
                state.Outcome = "Предыдущая версия C# восстановлена; ожидается повторная компиляция."; Save(state);
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (var file in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        AtomicBytes(file.Path, new UTF8Encoding(false).GetBytes(file.After));
                        // DeleteAsset also removes the GUID. Keep references intact on rollback.
                        if (metadata[file.Path] != null) File.WriteAllBytes(file.Path + ".meta", metadata[file.Path]);
                        if (FileHash(file.Path) != file.AfterSha256) throw new IOException("Проверка содержимого не пройдена.");
                    }
                    catch (Exception rollbackError) { failures.Add(file.Path + ": " + rollbackError.Message); }
                }
                state.Reverted = false; state.Restoring = false; state.Pending = false;
                state.Outcome = failures.Count == 0
                    ? "Отмена C# не выполнена. Все затронутые файлы возвращены к версии импорта; можно повторить отмену. " + error.Message
                    : "Отмена C# выполнена частично; автоматическое восстановление версии импорта не удалось. Проверьте файлы вручную: " + string.Join("; ", failures);
                try { Save(state); }
                catch (Exception saveError) { state.Outcome += " Не удалось сохранить состояние отмены: " + saveError.Message; }
                AssetDatabase.Refresh(); Changed?.Invoke();
                throw new InvalidOperationException(state.Outcome, error);
            }
            errors.Clear(); AssetDatabase.Refresh(); CompilationPipeline.RequestScriptCompilation(); Changed?.Invoke();
        }
    }
}
