using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit display test fixture. Selects a real fixed Game View size; never changes runtime UI/model state.</summary>
public static class NativeGameViewQaMenu
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [MenuItem("WorldWeaver/Acceptance/QA Game View/640x480")]
    public static void Compact() => Select(640, 480);
    [MenuItem("WorldWeaver/Acceptance/QA Game View/1920x1080")]
    public static void FullHd() => Select(1920, 1080);
    [MenuItem("WorldWeaver/Acceptance/QA Game View/Fit current view")]
    public static void FitCurrent()
    {
        Require(!Application.isBatchMode, "Use the normal GUI Editor.");
        var type = Need(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"), "UnityEditor.GameView"); var view = FindView(type);
        Write(new JObject { ["kind"] = "explicit-real-game-view-fit-display-fixture", ["fit"] = Fit(view), ["gameViewInstanceId"] = view.GetInstanceID(), ["changedConversationOrModel"] = false, ["customEntriesChanged"] = false, ["syntheticKeysOrCssViewportUsed"] = false });
    }
    [MenuItem("WorldWeaver/Acceptance/QA Game View/640x480", true)]
    [MenuItem("WorldWeaver/Acceptance/QA Game View/1920x1080", true)]
    [MenuItem("WorldWeaver/Acceptance/QA Game View/Fit current view", true)]
    static bool Enabled() => !Application.isBatchMode;

    static void Select(int width, int height)
    {
        Require(!Application.isBatchMode, "Use the normal GUI Editor for the explicit Game View display fixture.");
        string output = OutputDirectory(), source = Path.GetFullPath(SourcePath());
        Require(File.Exists(source), "The installed QA source must be readable for its SHA256.");
        var assembly = typeof(EditorWindow).Assembly;
        var viewType = Need(assembly.GetType("UnityEditor.GameView"), "UnityEditor.GameView");
        var sizesType = Need(assembly.GetType("UnityEditor.GameViewSizes"), "UnityEditor.GameViewSizes");
        var sizeType = Need(assembly.GetType("UnityEditor.GameViewSize"), "UnityEditor.GameViewSize");
        var sizeKind = Need(assembly.GetType("UnityEditor.GameViewSizeType"), "UnityEditor.GameViewSizeType");
        var view = FindView(viewType);
        var selected = Need(viewType.GetProperty("selectedSizeIndex", Flags), "selectedSizeIndex");
        var currentGroup = Need(viewType.GetProperty("currentSizeGroupType", Flags), "currentSizeGroupType");
        var singleton = Need(sizesType.BaseType.GetProperty("instance", Flags), "GameViewSizes.instance").GetValue(null);
        var groupKey = currentGroup.GetValue(view); var group = Method(sizesType, "GetGroup").Invoke(singleton, new[] { groupKey });
        var add = Method(group.GetType(), "AddCustomSize"); var save = Method(sizesType, "SaveToHDD");
        var constructor = Need(sizeType.GetConstructor(Flags, null, new[] { sizeKind, typeof(int), typeof(int), typeof(string) }, null), "fixed-size constructor");
        var fixedKind = Enum.Parse(sizeKind, "FixedResolution"); var before = Entries(group); int previousIndex = (int)selected.GetValue(view);
        string label = "WorldWeaver QA " + width + "x" + height; int index = -1;
        for (int i = 0; i < before.Count; i++)
        {
            var candidate = At(group, i);
            if ((string)Value(candidate, "baseText") == label && (int)Value(candidate, "width") == width && (int)Value(candidate, "height") == height && Value(candidate, "sizeType").Equals(fixedKind)) { index = i; break; }
        }
        bool added = index < 0;
        if (added)
        {
            if (Enumerable.Range(0, before.Count).Any(i => (string)Value(At(group, i), "baseText") == label)) label = "WW QA " + width + "x" + height + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
            index = before.Count; add.Invoke(group, new[] { constructor.Invoke(new object[] { fixedKind, width, height, label }) });
        }
        var after = Entries(group); bool preserved = after.Count == before.Count + (added ? 1 : 0) && Enumerable.Range(0, before.Count).All(i => JToken.DeepEquals(before[i], after[i]));
        Require(preserved, "Existing Game View entries unexpectedly changed; selection was not applied.");
        if (added) save.Invoke(singleton, null);
        Method(viewType, "SizeSelectionCallback").Invoke(view, new object[] { index, null }); var fit = Fit(view);
        var actual = At(group, (int)selected.GetValue(view));
        bool exact = (int)Value(actual, "width") == width && (int)Value(actual, "height") == height && Value(actual, "sizeType").Equals(fixedKind);
        var target = viewType.GetProperty("targetRenderSize", Flags)?.GetValue(view);
        var report = new JObject { ["kind"] = "explicit-real-game-view-fixed-size-display-fixture", ["recordedUtc"] = DateTime.UtcNow.ToString("o"), ["unity"] = Application.unityVersion,
            ["sourceSha256"] = Hash(File.ReadAllBytes(source)), ["isPlaying"] = Application.isPlaying, ["gameViewInstanceId"] = view.GetInstanceID(), ["sizeGroup"] = groupKey.ToString(),
            ["requestedWidth"] = width, ["requestedHeight"] = height, ["qaLabel"] = label, ["addedCustomEntry"] = added, ["previousIndex"] = previousIndex, ["selectedIndex"] = (int)selected.GetValue(view),
            ["selectedWidth"] = (int)Value(actual, "width"), ["selectedHeight"] = (int)Value(actual, "height"), ["selectedKind"] = Value(actual, "sizeType").ToString(),
            ["entryCountBefore"] = before.Count, ["entryCountAfter"] = after.Count, ["allPreviousEntriesPreserved"] = preserved,
            ["entriesBeforeSha256"] = Hash(Encoding.UTF8.GetBytes(before.ToString(Newtonsoft.Json.Formatting.None))), ["entriesAfterSha256"] = Hash(Encoding.UTF8.GetBytes(after.ToString(Newtonsoft.Json.Formatting.None))),
            ["selectedExactFixedResolution"] = exact, ["fit"] = fit, ["passed"] = exact && preserved && (bool)fit["selectedFrameFitsViewport"], ["changedConversationOrModel"] = false, ["syntheticKeysOrCssViewportUsed"] = false,
            ["scope"] = "Real Editor GameViewSizes FixedResolution selection plus native GameView default fit/center transform. No conversation, inference, UI Toolkit/CSS, framebuffer edits or desktop automation. The native screenshot and subsequent resolved UI audit independently verify the rendered layout." };
        if (target is Vector2 size) report["targetRenderSizeObservedAtMenu"] = new JObject { ["width"] = size.x, ["height"] = size.y };
        Directory.CreateDirectory(output); string path = Path.Combine(output, "native-game-view-size-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(report.ToString());
        Require((bool)report["passed"], "The actual selected Game View preset or native fit did not match the requested display fixture. See " + path);
        Debug.Log("Explicit Game View QA fixture: " + width + "x" + height + "; " + path);
    }
    static EditorWindow FindView(Type type)
    {
        var focused = type.BaseType.GetMethod("GetLastFocusedPlayModeView", Flags)?.Invoke(null, null) as EditorWindow;
        var windows = Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().ToArray();
        var view = focused && type.IsInstanceOfType(focused) ? focused : windows.Length == 1 ? windows[0] : null;
        Require(view, "Keep one Game View open or focus the intended Game View before invoking this menu."); return view;
    }
    static JObject Fit(EditorWindow view)
    {
        // API names/signatures verified in installed 6000.3.27f1 metadata; behavior checked against
        // https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GameView/GameView.cs
        var type = view.GetType(); Method(type, "UpdateZoomAreaAndParent").Invoke(view, null);
        var viewport = (Rect)Value(view, "viewInWindow"); var target = (Vector2)Value(view, "targetRenderSize");
        Require(viewport.width > 0 && viewport.height > 0 && target.x > 0 && target.y > 0, "A positive real Game View viewport/target is required.");
        var area = Need(type.GetField("m_ZoomArea", Flags), "m_ZoomArea").GetValue(view); var before = (Vector2)Value(area, "scale");
        float fit = (float)Method(type, "DefaultScaleForTargetInView").Invoke(view, new object[] { target, viewport.size });
        float largestFit = (float)Method(type, "ScaleThatFitsTargetInView").Invoke(view, new object[] { target, viewport.size });
        Require(!float.IsNaN(fit) && !float.IsInfinity(fit) && fit > 0, "Game View returned an invalid fit scale.");
        Method(area.GetType(), "SetTransform").Invoke(area, new object[] { Vector2.zero, Vector2.one * fit });
        Method(type, "EnforceZoomAreaConstraints").Invoke(view, null); view.Repaint();
        var after = (Vector2)Value(area, "scale"); var displayed = (Rect)Value(view, "targetInView");
        bool fits = displayed.width <= viewport.width + .75f && displayed.height <= viewport.height + .75f;
        return new JObject { ["nativeDefaultFitScale"] = fit, ["largestFittingScale"] = largestFit, ["scaleBeforeX"] = before.x, ["scaleBeforeY"] = before.y, ["scaleAfterX"] = after.x, ["scaleAfterY"] = after.y,
            ["viewportWidthPoints"] = viewport.width, ["viewportHeightPoints"] = viewport.height, ["targetWidthPixels"] = target.x, ["targetHeightPixels"] = target.y, ["displayedWidthPoints"] = displayed.width, ["displayedHeightPoints"] = displayed.height, ["selectedFrameFitsViewport"] = fits };
    }
    static void Write(JObject report)
    {
        report["recordedUtc"] = DateTime.UtcNow.ToString("o"); report["unity"] = Application.unityVersion; report["sourceSha256"] = Hash(File.ReadAllBytes(Path.GetFullPath(SourcePath()))); report["isPlaying"] = Application.isPlaying;
        report["passed"] = (bool)report["fit"]["selectedFrameFitsViewport"]; string output = OutputDirectory(); Directory.CreateDirectory(output);
        string path = Path.Combine(output, "native-game-view-fit-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(report.ToString());
        Require((bool)report["passed"], "The selected real Game View frame did not fit; see " + path); Debug.Log("Explicit Game View native fit QA: " + path);
    }
    static JArray Entries(object group)
    {
        int count = (int)Method(group.GetType(), "GetTotalCount").Invoke(group, null); var result = new JArray();
        for (int i = 0; i < count; i++)
        {
            var item = At(group, i); result.Add(new JObject { ["index"] = i, ["width"] = (int)Value(item, "width"), ["height"] = (int)Value(item, "height"), ["kind"] = Value(item, "sizeType").ToString(), ["labelSha256"] = Hash(Encoding.UTF8.GetBytes((string)Value(item, "baseText") ?? "")) });
        }
        return result;
    }
    static object At(object group, int index) => Method(group.GetType(), "GetGameViewSize").Invoke(group, new object[] { index });
    static object Value(object owner, string name) => Need(owner.GetType().GetProperty(name, Flags), name).GetValue(owner);
    static MethodInfo Method(Type type, string name) => Need(type.GetMethod(name, Flags), type.Name + "." + name);
    static T Need<T>(T value, string name) where T : class { Require(value != null, "Unsupported Editor API: " + name); return value; }
    static string OutputDirectory()
    {
        string output = Environment.GetEnvironmentVariable("WW_NATIVE_UI_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(output))
        {
            var project = Directory.GetParent(Application.dataPath); var repo = project?.Parent;
            Require(project?.Name == "UnityProject" && repo != null && Directory.Exists(Path.Combine(repo.FullName, "results")), "Set WW_NATIVE_UI_OUTPUT_DIR outside project Assets/Packages.");
            output = Path.Combine(repo.FullName, "results/screenshots-1.3");
        }
        output = Path.GetFullPath(output); string root = Directory.GetParent(Application.dataPath).FullName;
        Require(!Under(output, Application.dataPath) && !Under(output, Path.Combine(root, "Packages")), "QA receipts must remain outside project Assets/Packages."); return output;
    }
    static bool Under(string path, string root) => path == root || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    static string SourcePath([CallerFilePath] string path = "") => path;
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
