using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;
using Object = UnityEngine.Object;

/// <summary>Real rendered attachment and real local vision inference, with an empty live scene.</summary>
public static class VisualAcceptanceRunner
{
    public static async void Run()
    {
        string output = Environment.GetEnvironmentVariable("WW_VISION_OUTPUT");
        if (string.IsNullOrWhiteSpace(output) || Environment.GetEnvironmentVariable("WW_VISION_ACCEPTANCE") != "1")
            throw new InvalidOperationException("Explicit vision acceptance flag and output path required.");
        var report = new JObject { ["kind"] = "rendered-image-only-local-vision-acceptance", ["startedUtc"] = DateTime.UtcNow.ToString("o"),
            ["unity"] = Application.unityVersion, ["modelKey"] = "ollama:qwen3.5:9b", ["power"] = "Deep",
            ["providerFactoryInjected"] = false, ["expectedImage"] = "Red cube at image left; blue sphere at image right.",
            ["limitations"] = new JArray("One authored image and one actual local response; not a general vision benchmark.") };
        ChatController controller = null;
        bool passed = false;
        var watch = Stopwatch.StartNew();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var red = new Material(Shader.Find("Standard")) { color = Color.red };
            var blue = new Material(Shader.Find("Standard")) { color = Color.blue };
            red.SetFloat("_Glossiness", 0); blue.SetFloat("_Glossiness", 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .5f;
            var lightObject = new GameObject("Fixture light"); var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1; light.transform.rotation = Quaternion.Euler(35, -30, 0);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.position = new Vector3(-2, 0, 0);
            cube.transform.rotation = Quaternion.Euler(15, 25, 0); cube.GetComponent<Renderer>().sharedMaterial = red;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.transform.position = new Vector3(2, 0, 0);
            sphere.GetComponent<Renderer>().sharedMaterial = blue;
            var cameraObject = new GameObject("Fixture camera"); cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.orthographicSize = 2.5f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
            var attachment = AgentViewCapture.Capture();
            string imageOutput = Path.ChangeExtension(output, ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.Copy(attachment.Path, imageOutput, true);
            report["image"] = new JObject { ["path"] = imageOutput, ["sha256"] = attachment.Sha256, ["capture"] = "Actual Camera.Render via AgentViewCapture, 960x540" };
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Object.DestroyImmediate(red); Object.DestroyImmediate(blue); Selection.objects = Array.Empty<Object>();
            report["liveSceneObjectsBeforeInference"] = ProjectContextIndex.Objects().Count();
            report["groundTruthInUserPrompt"] = false;
            controller = new ChatController(historyFolder: "UserSettings/WorldWeaverVisionAcceptance/" + Guid.NewGuid().ToString("N"));
            await controller.RefreshModelsAsync();
            var selectedModel = controller.Models.Single(m => m.Key == "ollama:qwen3.5:9b");
            if (!selectedModel.Available || !selectedModel.SupportsVision) throw new InvalidOperationException("Actual local model capability discovery did not confirm vision.");
            report["capabilityVerification"] = "Actual Ollama /api/tags + /api/show via RefreshModelsAsync; available vision model required.";
            controller.Settings.ModelKey = "ollama:qwen3.5:9b"; controller.Settings.Power = AssistantPower.Deep;
            controller.Settings.Mode = AssistantMode.Ask; controller.SaveSettings();
            const string prompt = "Что изображено слева и справа? Назови форму и цвет каждого объекта. Ответь только в чате, сцену не меняй.";
            report["prompt"] = prompt;
            await controller.SendAsync(prompt, new[] { attachment });
            var answer = controller.Current.Messages.Last(m => m.Role == "assistant");
            string reply = answer.Text ?? "";
            string normal = reply.ToLowerInvariant().Replace('ё', 'е');
            bool content = normal.Contains("красн") && normal.Contains("куб") && normal.Contains("син") &&
                (normal.Contains("сфер") || normal.Contains("шар")) && normal.Contains("слева") && normal.Contains("справа");
            report["response"] = reply; report["status"] = answer.Status; report["isError"] = answer.IsError;
            report["attempts"] = JArray.FromObject(answer.Attempts); report["steps"] = JArray.FromObject(answer.Steps);
            report["liveSceneObjectsAfterInference"] = ProjectContextIndex.Objects().Count();
            report["acceptedContentWords"] = content;
            report["automatedCriterion"] = "Required shape/color/side words co-occur. Their semantic binding to left/right requires independent review.";
            report["manualSemanticReviewRequired"] = true;
            passed = !answer.IsError && content && answer.PendingPlan == null && ProjectContextIndex.Objects().Count() == 0;
        }
        catch (Exception error) { report["error"] = ChatHistoryStore.Redact(error.ToString()); }
        finally
        {
            controller?.Dispose(); watch.Stop(); report["ms"] = watch.ElapsedMilliseconds;
            report["finishedUtc"] = DateTime.UtcNow.ToString("o"); report["passed"] = passed;
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
