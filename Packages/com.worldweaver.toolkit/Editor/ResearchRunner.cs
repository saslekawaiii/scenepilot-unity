using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldWeaver.Editor
{
    /// <summary>Reproducible real-provider experiments. Never substitutes fixtures for inference.</summary>
    public static class ResearchRunner
    {
        private static string Root => Directory.GetParent(Application.dataPath).Parent.FullName;
        public static async void Run()
        {
            try
            {
                if (File.Exists("Assets/WorldWeaverDemo/WorldWeaverDemo.unity")) UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WorldWeaverDemo/WorldWeaverDemo.unity"); else DemoBuilder.Create();
                var npc = GameObject.Find("Merchant").GetComponent<IntelligentNPC>();
                var dataset = JArray.Parse(File.ReadAllText(Path.Combine(Root, "results/scenarios.json")));
                int limit = int.TryParse(Environment.GetEnvironmentVariable("WW_EVAL_LIMIT"), out var count) ? count : dataset.Count;
                string mode = Environment.GetEnvironmentVariable("WW_EVAL_PROVIDER") ?? "local";
                var configurations = mode == "cloud" ? new[] { "cloud-rag" } : new[] { "local-no-rag", "local-rag" };
                string output = Path.Combine(Root, "results/evaluation-" + mode + ".jsonl");
                using (var file = new StreamWriter(output, false))
                foreach (var configuration in configurations)
                {
                    var demo = GameObject.Find("Dialogue").GetComponent<DemoDialogue>();
                    var routes = mode == "cloud" ? demo.CloudRoutes : demo.LocalRoutes;
                    npc.Models = routes;
                    using (var pipeline = new NpcPipeline(new ModelRouter(routes), new RagIndex(new OllamaEmbeddingProvider(routes.EmbeddingEndpoint, routes.EmbeddingModel)), new ActionDispatcher()))
                    foreach (var scenario in dataset.Take(limit))
                    {
                        var row = new JObject { ["id"] = scenario["id"], ["configuration"] = configuration, ["question"] = scenario["question"], ["expected"] = scenario["expected"], ["source"] = scenario["source"], ["timestamp"] = DateTime.UtcNow.ToString("O") };
                        var watch = Stopwatch.StartNew();
                        try
                        {
                            var trace = await pipeline.RunAsync(npc, (string)scenario["question"], ModelTask.Dialogue, configuration != "local-no-rag", CancellationToken.None, false);
                            row["trace"] = JToken.FromObject(trace);
                            string answer = Normalize(trace.Speech);
                            row["answer_correct"] = scenario["expected"].Values<string>().Any(e => answer.Contains(Normalize(e)));
                            row["retrieval_hit"] = trace.Retrieved.Any(c => c.DocumentId == (string)scenario["source"]);
                            row["precision_at_3"] = trace.Retrieved.Take(3).Count(c => c.DocumentId == (string)scenario["source"]) / 3.0;
                            row["json_valid"] = true; row["action_batch_accepted"] = trace.Validation.Accepted;
                        }
                        catch (Exception error) { row["error"] = error is ProviderException || error is ArgumentException ? error.Message : error.GetType().Name; row["json_valid"] = false; row["answer_correct"] = false; if (TraceLog.Entries.Count > 0) row["trace"] = JToken.FromObject(TraceLog.Entries[TraceLog.Entries.Count - 1]); }
                        row["wall_ms"] = watch.ElapsedMilliseconds;
                        file.WriteLine(row.ToString(Formatting.None)); file.Flush();
                        Debug.Log("WorldWeaver experiment " + configuration + " " + scenario["id"] + " complete.");
                    }
                }
                MeasureTerrain();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Research runner: " + error.GetType().Name + " " + error.Message); EditorApplication.Exit(1); }
        }
        private static string Normalize(string text) => (text ?? "").ToLowerInvariant().Replace('ё', 'е');
        private static void MeasureTerrain()
        {
            var rows = new JArray();
            foreach (int resolution in new[] { 65, 129, 257, 513 })
            for (int i = 0; i < 10; i++)
            {
                var spec = new TerrainSpec { resolution = resolution, seed = 42 + i }; var watch = Stopwatch.StartNew();
                var first = TerrainGenerator.Generate(spec); long ms = watch.ElapsedTicks; var second = TerrainGenerator.Generate(spec);
                bool equal = first.Cast<float>().SequenceEqual(second.Cast<float>());
                rows.Add(new JObject { ["resolution"] = resolution, ["seed"] = spec.seed, ["elapsed_ms"] = ms * 1000.0 / Stopwatch.Frequency, ["identical"] = equal });
            }
            File.WriteAllText(Path.Combine(Root, "results/terrain.json"), rows.ToString(Formatting.Indented));
        }
    }
}
