using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Independent observers for preregistered new requests. No gameplay repairs,
// handler invocation, reflection-based field mutation, or injected solution code.
public sealed class NextAgentArtifactAcceptanceTests
{
    Keyboard keyboard;
    Mouse mouse;
    InputSettings.BackgroundBehavior previousBackground;
    InputSettings.EditorInputBehaviorInPlayMode previousRouting;
    bool inputSettingsSaved;
    bool previousRunInBackground;
    bool eventObserverInstalled;
    Key observedKey;
    int keyDownEvents,keyUpEvents,mouseDownEvents,mouseUpEvents;
    string receivedUpdateTypes="";

    void ObserveInputEvent(InputEventPtr inputEvent,InputDevice device)
    {
        if(!inputEvent.IsA<StateEvent>()&&!inputEvent.IsA<DeltaStateEvent>())return;
        string update=InputState.currentUpdateType.ToString();
        if(device==keyboard&&observedKey!=Key.None&&keyboard[observedKey].ReadValueFromEvent(inputEvent,out float keyValue))
        {
            if(keyValue>.5f)keyDownEvents++;else keyUpEvents++;
            if(!receivedUpdateTypes.Contains(update))receivedUpdateTypes+=(receivedUpdateTypes.Length==0?"":",")+update;
            Debug.Log($"NEXT_INPUT_EVENT device=keyboard id={device.deviceId} key={observedKey} value={keyValue:F1} update={update} handled={inputEvent.handled} frame={Time.frameCount}");
        }
        if(device==mouse&&mouse.leftButton.ReadValueFromEvent(inputEvent,out float mouseValue))
        {
            if(mouseValue>.5f)mouseDownEvents++;else mouseUpEvents++;
            if(!receivedUpdateTypes.Contains(update))receivedUpdateTypes+=(receivedUpdateTypes.Length==0?"":",")+update;
            Debug.Log($"NEXT_INPUT_EVENT device=mouse id={device.deviceId} left={mouseValue:F1} update={update} handled={inputEvent.handled} frame={Time.frameCount}");
        }
    }

    void LogInputDiagnostics(string stage)
    {
        var systems=UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        string details=string.Join(";",systems.Select(s=>s.name+"#"+s.GetInstanceID()+" active="+s.isActiveAndEnabled+" modules=["+string.Join(",",s.GetComponents<BaseInputModule>().Select(m=>m.GetType().FullName+" enabled="+m.enabled+" active="+m.gameObject.activeInHierarchy+" sameObject="+(m.gameObject==s.gameObject)))+"]"));
        var current=EventSystem.current;
        Debug.Log($"NEXT_INPUT_DIAGNOSTICS stage={stage} playing={Application.isPlaying}/{EditorApplication.isPlaying} paused={EditorApplication.isPaused} focused={Application.isFocused} background={Application.runInBackground} frame={Time.frameCount} updateMode={InputSystem.settings.updateMode} currentUpdateType={InputState.currentUpdateType} receivedUpdateTypes={receivedUpdateTypes} virtualKeyboard={keyboard?.deviceId} enabled={keyboard?.enabled} virtualMouse={mouse?.deviceId} enabled={mouse?.enabled} keyEvents={keyDownEvents}/{keyUpEvents} mouseEvents={mouseDownEvents}/{mouseUpEvents} currentEventSystem={(current?current.name+"#"+current.GetInstanceID():"null")} currentModule={(current&&current.currentInputModule?current.currentInputModule.GetType().FullName:"null")} systems={details}");
    }

    void OpenSceneForCase(string caseName)
    {
        Assert.AreEqual("1", Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),
            Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT") ?? ""));
        string folder = Environment.GetEnvironmentVariable("WW_NEXT_ARTIFACT_FOLDER");
        Assert.IsTrue(!string.IsNullOrEmpty(folder) && folder.StartsWith("Assets/", StringComparison.Ordinal)
            && !folder.Contains(".."), "Explicit model artifact folder below Assets required");
        string scene = folder.TrimEnd('/') + "/" + caseName + ".unity";
        Assert.IsTrue(File.Exists(scene), "No saved model-authored scene for " + caseName);
        EditorSceneManager.OpenScene(scene);
    }

    IEnumerator PrepareInputAfterEntry()
    {
        Assert.IsTrue(Application.isPlaying&&EditorApplication.isPlaying,"Fixture has not entered actual Play Mode");
        InstallInput();
        // EditMode coroutine ticks are not necessarily PlayerLoop frames. Wait
        // for actual game time so Start, EventSystem.Update and input routing
        // have run before judging model-authored behavior.
        yield return WaitActualSeconds(.2f);
        LogInputDiagnostics("startup");
    }

    void InstallInput()
    {
        Debug.Log($"NEXT_FIXTURE_START frame={Time.frameCount} gameTime={Time.unscaledTime:F3} paused={EditorApplication.isPaused} focused={Application.isFocused} runInBackground={Application.runInBackground} timeScale={Time.timeScale:F3}");
        previousRunInBackground = Application.runInBackground;
        previousBackground = InputSystem.settings.backgroundBehavior;
        previousRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
        inputSettingsSaved = true;
        // Fixture environment only: a locked/unfocused headless host must still
        // run the real PlayerLoop. Restored before each Play exit and teardown.
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard.MakeCurrent();
        mouse.MakeCurrent();
        keyDownEvents=keyUpEvents=mouseDownEvents=mouseUpEvents=0;
        observedKey=Key.None;receivedUpdateTypes="";
        InputSystem.onEvent+=ObserveInputEvent;eventObserverInstalled=true;
        LogInputDiagnostics("devices-installed");
    }

    void RemoveInput()
    {
        if(eventObserverInstalled){InputSystem.onEvent-=ObserveInputEvent;eventObserverInstalled=false;}
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        keyboard = null;
        mouse = null;
        if (inputSettingsSaved)
        {
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousRouting;
            Application.runInBackground = previousRunInBackground;
            inputSettingsSaved = false;
        }
    }

    [UnityTearDown] public IEnumerator Close()
    {
        RemoveInput();
        Time.timeScale = 1;
        if (EditorApplication.isPlaying) yield return new ExitPlayMode();
    }

    IEnumerator Press(Key key)
    {
        keyboard.MakeCurrent();observedKey=key;
        int beforeDown=keyDownEvents,beforeUp=keyUpEvents;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return WaitActualSeconds(.06f);
        LogInputDiagnostics("key-down");
        Assert.Greater(keyDownEvents,beforeDown,"Fixture keyboard-down state event was not observed for the queued virtual device");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return WaitActualSeconds(.06f);
        LogInputDiagnostics("key-up");
        Assert.Greater(keyUpEvents,beforeUp,"Fixture keyboard-up state event was not observed for the queued virtual device");
        observedKey=Key.None;
    }

    static IEnumerator WaitActualSeconds(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        double deadline = EditorApplication.timeSinceStartup + seconds + 15;
        while (Time.unscaledTime < until && EditorApplication.timeSinceStartup < deadline)
        {
            // Schedule an actual Unity PlayerLoop update; do not call gameplay
            // handlers, InputSystem.Update or Physics.Simulate ourselves.
            EditorApplication.QueuePlayerLoopUpdate();
            yield return null;
        }
        Assert.GreaterOrEqual(Time.unscaledTime, until - .03f,
            $"Fixture PlayerLoop did not advance: frame={Time.frameCount} gameTime={Time.unscaledTime:F3} paused={EditorApplication.isPaused} focused={Application.isFocused} runInBackground={Application.runInBackground} timeScale={Time.timeScale:F3}");
    }

    static string TextOf(Component c)
    {
        var text = c.GetType().GetProperty("text");
        return text != null && text.PropertyType == typeof(string) ? (string)text.GetValue(c) : null;
    }

    static string[] VisibleText() => UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
        .Where(c => c && c.gameObject.activeInHierarchy && (!(c is Behaviour b) || b.isActiveAndEnabled))
        .Select(TextOf).Where(t => t != null).Select(t => t.Trim()).ToArray();

    static void RequireStatus(string expected) => Assert.IsTrue(VisibleText().Contains(expected),
        "Actual active UI does not display the required status: " + expected);

    static string ButtonLabel(Button button) => string.Join(" ", button.GetComponentsInChildren<Component>(true)
        .Where(c => c).Select(TextOf).Where(t => t != null)).Trim();

    static Button ButtonNamed(string label)
    {
        var matches = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Where(b => b.isActiveAndEnabled && b.interactable && ButtonLabel(b) == label).ToArray();
        Assert.AreEqual(1, matches.Length, "Expected one actual interactable button: " + label);
        return matches[0];
    }

    IEnumerator Click(string label)
    {
        Button button = ButtonNamed(label);
        var canvas = button.GetComponentInParent<Canvas>();
        Assert.IsNotNull(canvas);
        Canvas.ForceUpdateCanvases();
        var rect = button.GetComponent<RectTransform>();
        Vector2 point = RectTransformUtility.WorldToScreenPoint(
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            rect.TransformPoint(rect.rect.center));
        mouse.MakeCurrent();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return WaitActualSeconds(.06f);
        LogInputDiagnostics("pointer-hover");
        Assert.IsTrue(EventSystem.current&&EventSystem.current.currentInputModule,
            "Actual EventSystem input module is missing after an actual pointer hover event");
        int beforeDown=mouseDownEvents,beforeUp=mouseUpEvents;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return WaitActualSeconds(.06f);
        Assert.Greater(mouseDownEvents,beforeDown,"Fixture mouse-down state event was not observed for the queued virtual device");
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return WaitActualSeconds(.06f);
        Assert.Greater(mouseUpEvents,beforeUp,"Fixture mouse-up state event was not observed for the queued virtual device");
        LogInputDiagnostics("pointer-click-complete");
    }

    static int Score()
    {
        var scores = VisibleText().Select(t => Regex.Match(t, @"^Очки\s*:\s*(\d+)$"))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToArray();
        Assert.AreEqual(1, scores.Length, "Expected one unambiguous visible score label 'Очки: N'");
        return scores[0];
    }

    [UnityTest] public IEnumerator DoorRespondsToActualKeyboardLockAndLeavesPhysicalPassage()
    {
        OpenSceneForCase("hinged_door");
        // Domain-reload yield instructions MUST be yielded directly by this
        // UnityTest, not hidden in a nested IEnumerator helper.
        yield return new EnterPlayMode();
        yield return PrepareInputAfterEntry();
        // Captured observation locals must be allocated after the domain reload.
        // This helper contains no Enter/ExitPlayMode yield instructions.
        yield return ObserveDoorAfterEntry();
    }

    IEnumerator ObserveDoorAfterEntry()
    {
        Assert.IsTrue(Application.isPlaying&&EditorApplication.isPlaying,"Door observation requires actual Play Mode");
        var panels = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(v => v.enabled && v.gameObject.activeInHierarchy && Mathf.Abs(v.bounds.size.x - 1.2f) < .06f
                && Mathf.Abs(v.bounds.size.y - 2) < .06f && Mathf.Abs(v.bounds.size.z - .12f) < .04f).ToArray();
        Assert.AreEqual(1, panels.Length, "Identify the requested visible door by its closed dimensions");
        Renderer panel = panels[0];
        Quaternion closedRotation = panel.transform.rotation;
        Vector3 closedPosition = panel.transform.position;
        Bounds closedBounds = panel.bounds;
        var colliders = panel.GetComponentsInChildren<Collider>().Concat(panel.GetComponentsInParent<Collider>())
            .Where(c => c && c.enabled && !c.isTrigger && c.bounds.Intersects(closedBounds)).Distinct().ToArray();
        // An equally valid generated setup may place the physical panel as a sibling.
        if (colliders.Length == 0)
            colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.enabled && !c.isTrigger && Vector3.Distance(c.bounds.center, closedBounds.center) < .1f
                    && Vector3.Distance(c.bounds.size, closedBounds.size) < .15f).ToArray();
        Assert.IsNotEmpty(colliders, "Visible door has no actual solid collider");
        var passageRay = new Ray(closedBounds.center - Vector3.forward * 2, Vector3.forward);
        Func<bool> blocked = () => { Physics.SyncTransforms(); return colliders.Any(c => c && c.enabled && c.Raycast(passageRay, out var hit, 4)); };
        Assert.IsTrue(blocked(), "Closed door does not physically block the central passage");
        Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .Any(c => c.enabled && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(c), closedBounds)),
            "No enabled gameplay camera sees the requested panel");
        Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l => l.enabled));
        RequireStatus("Закрыта");
        yield return Press(Key.L);
        RequireStatus("Заперта");
        yield return Press(Key.Space);
        yield return WaitActualSeconds(1.3f);
        Assert.Less(Quaternion.Angle(closedRotation, panel.transform.rotation), 1,
            "Locked door moved after actual Space input");
        Assert.IsTrue(blocked());
        yield return Press(Key.L);
        RequireStatus("Закрыта");
        yield return Press(Key.Space);
        yield return WaitActualSeconds(.35f);
        Assert.That(Quaternion.Angle(closedRotation, panel.transform.rotation), Is.InRange(5f, 85f),
            "Door must visibly move through intermediate poses, not teleport or wait at the endpoint");
        Assert.Greater(Vector3.Dot(panel.transform.up, Vector3.up), .99f, "Door tilts away from the vertical hinge axis");
        yield return WaitActualSeconds(.95f);
        Assert.AreEqual(90, Quaternion.Angle(closedRotation, panel.transform.rotation), 3,
            "Actual panel did not complete the requested ninety-degree opening");
        Assert.IsFalse(blocked(), "Opened door still blocks the central passage");
        RequireStatus("Открыта");
        yield return Press(Key.Space);
        yield return WaitActualSeconds(1.3f);
        Assert.Less(Quaternion.Angle(closedRotation, panel.transform.rotation), 1);
        Assert.Less(Vector3.Distance(closedPosition, panel.transform.position), .025f,
            "Closed panel did not return to its actual original pose");
        Assert.IsTrue(blocked());
        RequireStatus("Закрыта");
        Debug.Log("NEXT_DOOR_INPUT real L lock/unlock and Space open/close; actual ninety-degree panel rotation; closed ray blocked, open ray clear; original closed pose restored; no repair by observer");
    }

    [UnityTest] public IEnumerator CounterUsesActualPointerClampsAndPersistsAcrossPlayRestarts()
    {
        OpenSceneForCase("persistent_counter");
        // Domain-reload yield instructions MUST be yielded directly by this
        // UnityTest, not hidden in a nested IEnumerator helper.
        yield return new EnterPlayMode();
        yield return PrepareInputAfterEntry();
        yield return Click("Сброс");
        Assert.AreEqual(0, Score());
        yield return Click("-1");
        Assert.AreEqual(0, Score(), "Counter crossed requested lower bound");
        for (int i = 1; i <= 4; i++)
        {
            yield return Click("+1");
            Assert.AreEqual(i, Score(), "An actual pointer click did not increment exactly once");
        }
        yield return Click("-1");
        Assert.AreEqual(3, Score());
        RemoveInput();
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        InstallInput();
        yield return null;
        yield return null;
        yield return WaitActualSeconds(.2f);
        Assert.AreEqual(3, Score(), "Score was not restored after an actual Play Mode restart");
        for (int i = 0; i < 10; i++) yield return Click("+1");
        Assert.AreEqual(9, Score(), "Counter crossed upper bound or missed repeated actual clicks");
        yield return Click("-1");
        yield return Click("-1");
        Assert.AreEqual(7, Score());
        yield return Click("Сброс");
        Assert.AreEqual(0, Score());
        RemoveInput();
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        InstallInput();
        yield return null;
        yield return null;
        yield return WaitActualSeconds(.2f);
        Assert.AreEqual(0, Score(), "Reset score was not persisted across the second Play Mode restart");
        Debug.Log("NEXT_COUNTER_INPUT actual pointer +1/-1/reset, bounds 0..9, exactly one increment per click, persisted 3 across first Play restart and persisted reset 0 across second; no handler invocation or game-state repair");
    }
}
