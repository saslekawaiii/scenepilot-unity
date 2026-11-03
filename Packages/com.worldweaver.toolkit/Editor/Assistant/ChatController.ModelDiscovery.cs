using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace WorldWeaver.Editor.Assistant
{
    public sealed partial class ChatController
    {
        OllamaModelDiscovery localDiscovery;
        readonly CancellationTokenSource modelDiscoveryLifetime = new CancellationTokenSource();
        Task localDiscoveryTask;
        bool automaticLocalDiscovery;
        double nextLocalDiscovery;
        public bool LocalDiscoveryRunning => localDiscoveryTask != null && !localDiscoveryTask.IsCompleted;
        public string LocalDiscoveryStatus { get; private set; } = "Локальные модели появятся автоматически после запуска Ollama.";
        public DateTime? LastLocalDiscoveryUtc { get; private set; }

        void InitializeModelDiscovery(bool automatic, OllamaModelDiscovery discovery)
        {
            localDiscovery = discovery ?? new OllamaModelDiscovery();
            automaticLocalDiscovery = automatic;
            if (automatic) EditorApplication.update += PollLocalModels;
            AssemblyReloadEvents.beforeAssemblyReload += StopModelDiscovery;
        }

        void PollLocalModels()
        {
            if (disposed || !automaticLocalDiscovery || LocalDiscoveryRunning || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || HasPendingWork || EditorApplication.timeSinceStartup < nextLocalDiscovery) return;
            // The task owns/catches discovery errors; no async void and no network calls inside an editor frame.
            _ = RefreshLocalModelsAsync();
        }

        public Task RefreshLocalModelsAsync()
        {
            if (disposed || modelDiscoveryLifetime.IsCancellationRequested) return Task.CompletedTask;
            if (LocalDiscoveryRunning) return localDiscoveryTask;
            return localDiscoveryTask = RefreshLocalModelsCoreAsync();
        }

        async Task RefreshLocalModelsCoreAsync()
        {
            bool available = false;
            try
            {
                var result = await localDiscovery.RefreshAsync(modelDiscoveryLifetime.Token);
                if (disposed || modelDiscoveryLifetime.IsCancellationRequested) return;
                available = result.ServerAvailable; LastLocalDiscoveryUtc = result.CheckedUtc; LocalDiscoveryStatus = result.Message;
                MergeLocalModels(result);
                // Background discovery must not overwrite the result/progress of an agent task.
                if (!Busy && (Phase == "Проверка моделей…" || Phase.StartsWith("Ollama", StringComparison.Ordinal)))
                    Phase = Connected ? "Готов к работе" : result.Message;
                Notify();
            }
            catch (OperationCanceledException) when (modelDiscoveryLifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!disposed) { LocalDiscoveryStatus = "Не удалось обновить Ollama: " + ChatHistoryStore.Redact(error.Message); Notify(); }
            }
            finally { nextLocalDiscovery = EditorApplication.timeSinceStartup + (available ? 10 : 30); }
        }

        void MergeLocalModels(OllamaDiscoveryResult result)
        {
            var old = models.Where(m => m.Provider == ProviderKind.Ollama && string.IsNullOrEmpty(m.ConnectionId)).ToList();
            var found = new List<AvailableModel>();
            if (result.ServerAvailable)
            {
                foreach (var item in result.Models)
                {
                    if (item.CapabilitiesKnown && !item.Completion) continue; // Embedding-only and other non-chat models are not selectable.
                    string state = !item.CapabilitiesKnown ? "поддержка чата ещё проверяется" : item.Loaded ? "загружена в память" : result.LoadedStateKnown ? "установлена · загрузится при запросе" : "установлена · состояние памяти неизвестно";
                    found.Add(new AvailableModel { Key = "ollama:" + item.Id, Name = item.Id + (string.IsNullOrEmpty(item.Parameters) ? "" : " · " + item.Parameters),
                        Model = item.Id, Endpoint = localDiscovery.Endpoint, Provider = ProviderKind.Ollama,
                        Available = item.CapabilitiesKnown && item.Completion, SupportsVision = item.Vision, Loaded = item.Loaded, AutoDiscovered = true, Remote=item.Remote, SizeBytes=item.Bytes,
                        Description = (item.Remote ? "Ollama · облачная модель, квота сервера" : "На этом компьютере · " + (item.Bytes / 1e9).ToString("F1") + " ГБ") + " · " + state });
                }
            }
            var selected = new HashSet<string>(sessions.Select(s => s.Settings.ModelKey), StringComparer.Ordinal);
            foreach (var previous in old.Where(m => !found.Any(f => f.Key == m.Key) && (!result.ServerAvailable || selected.Contains(m.Key))))
            {
                previous.Available = false; previous.Loaded = false; previous.SupportsVision = false;
                previous.Description = result.ServerAvailable ? "Выбранная модель удалена, не установлена или не поддерживает чат. Выберите другую модель." : result.Message + " Прежняя модель сохранена; список обновится автоматически.";
                found.Add(previous);
            }
            models.RemoveAll(m => m.Provider == ProviderKind.Ollama && string.IsNullOrEmpty(m.ConnectionId));
            models.InsertRange(0, found.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        }

        void StopModelDiscovery()
        {
            EditorApplication.update -= PollLocalModels;
            AssemblyReloadEvents.beforeAssemblyReload -= StopModelDiscovery;
            automaticLocalDiscovery = false;
            if (!modelDiscoveryLifetime.IsCancellationRequested) modelDiscoveryLifetime.Cancel();
            localDiscovery?.Dispose();
        }
    }
}
