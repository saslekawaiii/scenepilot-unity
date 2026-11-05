using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class UIInteractionDiagnosticsTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        static JObject Review(IEnumerable<GameObject> sceneObjects)
        {
            var implementation=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.UIInteractionDiagnostics",true);
            return (JObject)implementation.GetMethod("ReviewSceneEvidence",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{sceneObjects});
        }
        [TearDown] public void Cleanup(){for(int i=objects.Count-1;i>=0;i--)if(objects[i])UnityEngine.Object.DestroyImmediate(objects[i]);objects.Clear();}
        GameObject Object(string name,params Type[] components){var go=new GameObject(name,components);objects.Add(go);return go;}
        Canvas Canvas(bool raycaster=true)
        {
            var go=Object("UGUI Canvas",typeof(RectTransform),typeof(Canvas));var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;if(raycaster)go.AddComponent<GraphicRaycaster>();return canvas;
        }
        Button Button(Canvas canvas,string name="Actual button")
        {
            var go=Object(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(canvas.transform,false);var button=go.GetComponent<Button>();button.targetGraphic=go.GetComponent<Image>();return button;
        }
        Behaviour Module(EventSystem events,bool assignDefaults=true)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule")).FirstOrDefault(t=>t!=null)??Assembly.Load("Unity.InputSystem").GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule",true);
            var module=(Behaviour)events.gameObject.AddComponent(type);
            type.GetMethod(assignDefaults?"AssignDefaultActions":"UnassignActions",BindingFlags.Public|BindingFlags.Instance).Invoke(module,null);return module;
        }
        static bool HasWarning(JObject result,string code)=>((JArray)result["warnings"]).Any(w=>(string)w["code"]==code);

        [Test] public void NoUguiIsNotDiagnosedAsAnInputError()
        {
            Object("World object",typeof(Light));var result=Review(objects);
            Assert.That((bool)result["uguiPresent"],Is.False);Assert.That(((JArray)result["warnings"]).Count,Is.Zero);Assert.That((bool)result["inputWasPolled"],Is.False);
        }
        [Test] public void MissingEventSystemIsStructuralEvidenceForInteractiveUgui()
        {
            Button(Canvas());var result=Review(objects);Assert.That(HasWarning(result,"UGUI_EVENT_SYSTEM_NOT_FOUND"),Is.True);Assert.That((int)result["counts"]["interactiveSelectables"],Is.EqualTo(1));
        }
        [Test] public void StaticCanvasDoesNotRequireAnEventSystem()
        {
            Canvas();var result=Review(objects);Assert.That((bool)result["uguiPresent"],Is.True);Assert.That((int)result["counts"]["interactiveSelectables"],Is.Zero);Assert.That(HasWarning(result,"UGUI_EVENT_SYSTEM_NOT_FOUND"),Is.False);
        }
        [Test] public void MissingEnabledModuleIsDistinguishedFromNullCurrentModule()
        {
            Button(Canvas());Object("Event System",typeof(EventSystem));var result=Review(objects);
            Assert.That(HasWarning(result,"UGUI_ENABLED_MODULE_NOT_FOUND"),Is.True);StringAssert.Contains("null is not a structural error",(string)result["eventSystems"][0]["runtimeObservation"]["interpretation"]);
        }
        [Test] public void ConfiguredInputSystemModuleIsReadWithoutPollingOrEnablingAnything()
        {
            Button(Canvas());var events=Object("Event System",typeof(EventSystem)).GetComponent<EventSystem>();var module=Module(events);string before=EditorJsonUtility.ToJson(module);var result=Review(objects);var facts=result["eventSystems"][0]["modules"][0];
            Assert.That((bool)facts["point"]["referencePresent"],Is.True);Assert.That((bool)facts["leftClick"]["referencePresent"],Is.True);Assert.That((int)facts["point"]["bindingCount"],Is.GreaterThan(0));
            Assert.That(HasWarning(result,"UGUI_ENABLED_MODULE_NOT_FOUND"),Is.False);Assert.That(HasWarning(result,"POINTER_ACTION_REFERENCES_ABSENT"),Is.False);Assert.That((bool)result["moduleActivationWasPolled"],Is.False);Assert.That((bool)result["actionsEnabledByInspection"],Is.False);Assert.That(EditorJsonUtility.ToJson(module),Is.EqualTo(before));
            Assert.That((bool)result["eventSystems"][0]["runtimeObservation"]["applicationIsPlaying"],Is.False);StringAssert.Contains("null is not a structural error",(string)result["eventSystems"][0]["runtimeObservation"]["interpretation"]);
        }
        [Test] public void UnassignedActionsAreReportedWithoutAssigningDefaultsDuringInspection()
        {
            Button(Canvas());var events=Object("Event System",typeof(EventSystem)).GetComponent<EventSystem>();var module=Module(events,false);string before=EditorJsonUtility.ToJson(module);var result=Review(objects);
            Assert.That((bool)result["eventSystems"][0]["modules"][0]["point"]["referencePresent"],Is.False);Assert.That(HasWarning(result,"POINTER_ACTION_REFERENCES_ABSENT"),Is.True);Assert.That(EditorJsonUtility.ToJson(module),Is.EqualTo(before));
        }
        [Test] public void DisabledModuleCannotCountAsAnEnabledStructuralRoute()
        {
            Button(Canvas());var events=Object("Event System",typeof(EventSystem)).GetComponent<EventSystem>();var module=Module(events);module.enabled=false;var result=Review(objects);
            Assert.That((int)result["eventSystems"][0]["enabledModuleCount"],Is.Zero);Assert.That((bool)result["eventSystems"][0]["modules"][0]["enabled"],Is.False);Assert.That(HasWarning(result,"UGUI_ENABLED_MODULE_NOT_FOUND"),Is.True);
        }
        [Test] public void DisabledGraphicRaycasterIsReportedWithoutSimulatedPointerClaims()
        {
            var canvas=Canvas();canvas.GetComponent<GraphicRaycaster>().enabled=false;Button(canvas);var result=Review(objects);
            Assert.That((int)result["canvases"][0]["activeRaycasterCount"],Is.Zero);Assert.That(HasWarning(result,"UGUI_GRAPHIC_RAYCASTER_NOT_FOUND"),Is.True);Assert.That((bool)result["observedOnlyNotPointerTest"],Is.True);
        }
        [Test] public void ChildGraphicCanProvidePointerCandidateWhenTargetGraphicDoesNot()
        {
            var button=Button(Canvas());button.targetGraphic.raycastTarget=false;var child=Object("Raycast child",typeof(RectTransform),typeof(Image));child.transform.SetParent(button.transform,false);child.GetComponent<Image>().raycastTarget=true;var result=Review(objects);
            Assert.That((int)result["buttons"][0]["activeRaycastGraphicCandidates"],Is.EqualTo(1));Assert.That(HasWarning(result,"NO_STANDARD_POINTER_GRAPHIC"),Is.False);
        }
        [Test] public void RuntimeListenersAreNotMisreportedAsMissingCallbacksOrInvoked()
        {
            int callbacks=0;var button=Button(Canvas());button.onClick.AddListener(()=>callbacks++);var result=Review(objects);
            Assert.That((int)result["buttons"][0]["persistentCallbackCount"],Is.Zero);Assert.That((bool)result["buttons"][0]["runtimeCallbackCountKnown"],Is.False);Assert.That(callbacks,Is.Zero);StringAssert.Contains("zero persistent callbacks is not an error",(string)result["callbackCountMeaning"]);
        }
        [Test] public void CanvasGroupFlagsAreMeasuredWithoutPretendingTheyProveDispatch()
        {
            var canvas=Canvas();var group=canvas.gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;Button(canvas);var result=Review(objects);var facts=result["buttons"][0]["canvasGroups"][0];
            Assert.That((bool)facts["interactable"],Is.False);Assert.That((bool)facts["blocksRaycasts"],Is.False);Assert.That((bool)result["observedOnlyNotPointerTest"],Is.True);
        }
        [Test] public void LegacyModuleConfigurationIsNeverPolledForAxisOrActivationState()
        {
            Button(Canvas());Object("Legacy Event System",typeof(EventSystem),typeof(StandaloneInputModule));Assert.DoesNotThrow(()=>Review(objects));var result=Review(objects);
            Assert.That((string)result["eventSystems"][0]["modules"][0]["inputBackend"],Is.EqualTo("Legacy Input Manager"));Assert.That((bool)result["inputWasPolled"],Is.False);Assert.That((bool)result["moduleActivationWasPolled"],Is.False);
        }
        [Test] public void DetailArraysAndTotalOutputAreBoundedWhileCountsRemainActual()
        {
            var canvas=Canvas();for(int i=0;i<30;i++)Button(canvas,"Button "+i+new string('x',300));var result=Review(objects);
            Assert.That((int)result["counts"]["buttons"],Is.EqualTo(30));Assert.That(((JArray)result["buttons"]).Count,Is.LessThanOrEqualTo(8));Assert.That(result.ToString(Newtonsoft.Json.Formatting.None).Length,Is.LessThanOrEqualTo(16000));
        }
    }
}
