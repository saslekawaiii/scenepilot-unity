using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Per-project user history, outside Assets and source control. No connection credentials.</summary>
    public sealed class ChatHistoryStore
    {
        private readonly string folder;
        public ChatHistoryStore(string root = null)
        { folder = Path.GetFullPath(root ?? "UserSettings/WorldWeaverChat"); Directory.CreateDirectory(folder); }
        public string Folder => folder;

        public List<ChatSession> Load()
        {
            var sessions = new List<ChatSession>();
            foreach (string path in Directory.GetFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
            {
                try
                {
                    var session = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(path), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 32 });
                    if (session != null && ValidId(session.Id) && session.Messages != null && session.Messages.Count <= 300)
                    {
                        if (session.Settings == null) session.Settings = new ChatSettings();
                        if (!Enum.IsDefined(typeof(AssistantPower), session.Settings.Power)) session.Settings.Power = AssistantPower.Balanced;
                        if (string.IsNullOrWhiteSpace(session.Settings.ModelKey)) session.Settings.ModelKey = new ChatSettings().ModelKey;
                        session.Title = Redact(session.Title ?? "Новый чат");
                        session.Messages.RemoveAll(m => m == null);
                        foreach (var message in session.Messages)
                        {
                            if (!ValidId(message.Id)) message.Id = Guid.NewGuid().ToString("N");
                            message.Text = Redact(message.Text); message.Status = message.Status ?? ""; message.Model = message.Model ?? "";
                            if (message.Role != "user") message.Role = "assistant";
                            if (message.Attempts == null) message.Attempts = new List<EditorPlanAttempt>();
                            if (message.Steps == null) message.Steps = new List<EditorCommandResult>();
                            message.Steps.RemoveAll(step => step == null);
                            foreach (var step in message.Steps)
                            {
                                step.Summary = Redact(step.Summary); step.Error = Redact(step.Error); step.Target = Redact(step.Target);
                                if (step.CreatedAssets == null) step.CreatedAssets = new List<string>();
                            }
                            if (message.PendingPlan != null && (message.PendingPlan.actions == null || message.PendingPlan.actions.Any(a => a == null || a.args == null)))
                            { message.PendingPlan = null; message.Status = "Повреждённый план: запросите новый"; }
                        }
                        sessions.Add(session);
                    }
                }
                catch (IOException) { /* A file being replaced is retried at the next load. */ }
                catch (JsonException) { /* Keep the damaged source intact; do not destroy a user's history. */ }
            }
            return sessions.OrderByDescending(s => s.UpdatedUtc, StringComparer.Ordinal).ToList();
        }

        public void Save(ChatSession session)
        {
            if (!ValidId(session.Id)) throw new ArgumentException("Invalid chat id.");
            session.UpdatedUtc = DateTime.UtcNow.ToString("o");
            if (session.Messages.Count > 250) session.Messages.RemoveRange(0, session.Messages.Count - 250);
            string destination = Path.Combine(folder, session.Id + ".json");
            string temp = destination + ".tmp";
            File.WriteAllText(temp, Redact(JsonConvert.SerializeObject(session, Formatting.Indented)), new UTF8Encoding(false));
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }

        public static bool ValidId(string id) => id != null && Regex.IsMatch(id, "^[a-f0-9]{32}$");
        public static string Redact(string text)
        {
            text = text ?? "";
            return Regex.Replace(text, @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|gsk_[A-Za-z0-9]{20,})", "[секрет скрыт]");
        }
    }
}
