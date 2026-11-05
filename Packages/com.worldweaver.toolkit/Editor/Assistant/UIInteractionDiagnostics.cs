using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WorldWeaver.Editor.Assistant
{
    // Structural read-back only. Never runs a module, polls legacy input,
    // dispatches events, enables actions or repairs the scene.
    internal static class UIInteractionDiagnostics
    {
        const int MaximumObjects=4096,MaximumCharacters=16000;
        static string Short(string value,int length=120)=>ChatHistoryStore.Redact((value??"").Substring(0,Math.Min(length,(value??"").Length)));
        static string Name(UnityEngine.Object value)=>value?Short(value.name,80):null;
        static string Target(GameObject go)=>go?ProjectContextIndex.StableTarget(go):null;
        static JObject Asset(UnityEngine.Object asset)=>asset?new JObject{["name"]=Name(asset),["path"]=Short(AssetDatabase.GetAssetPath(asset),240)}:null;
        static bool LegacyCompiled
        {
            get {
#if ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }
        static bool NewInputCompiled
        {
            get {
#if ENABLE_INPUT_SYSTEM
                return true;
#else
                return false;
#endif
            }
        }
        static void Warning(JArray warnings,string code,string target,string message)
        {if(warnings.Count<12)warnings.Add(new JObject{["code"]=code,["target"]=target,["message"]=message});}
        static JObject Action(InputActionReference reference)
        {
            var result=new JObject{["referencePresent"]=(bool)reference};if(!reference)return result;
            result["reference"]=Asset(reference);
            try
            {
                result["asset"]=Asset(reference.asset);var action=reference.action;result["resolvesToAction"]=action!=null;
                if(action!=null)
                {
                    result["actionName"]=Short(action.name);result["actionType"]=action.type.ToString();result["expectedControlType"]=Short(action.expectedControlType);result["enabledObservedNow"]=action.enabled;
                    result["bindingCount"]=action.bindings.Count;result["bindingPaths"]=new JArray(action.bindings.Take(4).Select(b=>Short(b.effectivePath,128)));
                }
            }
            catch(Exception error){result["resolutionError"]=Short(error.GetType().Name+": "+error.Message,240);}
            return result;
        }
        static JObject Module(BaseInputModule module,JArray warnings,bool interactiveUgui)
        {
            var value=new JObject{["type"]=module.GetType().FullName,["enabled"]=module.enabled,["activeInHierarchy"]=module.gameObject.activeInHierarchy,["activeAndEnabled"]=module.isActiveAndEnabled};
            if(module is InputSystemUIInputModule input)
            {
                value["inputBackend"]="Input System";value["actionsAsset"]=Asset(input.actionsAsset);value["point"]=Action(input.point);value["leftClick"]=Action(input.leftClick);value["moveReferencePresent"]=(bool)input.move;value["submitReferencePresent"]=(bool)input.submit;value["pointerBehavior"]=input.pointerBehavior.ToString();
                value["configurationIsNotDispatchProof"]=true;
                if(interactiveUgui&&module.isActiveAndEnabled&&(!input.point||!input.leftClick))
                    Warning(warnings,"POINTER_ACTION_REFERENCES_ABSENT",Target(module.gameObject),"Enabled Input System UI module has missing point/click references. Keyboard/XR-only UI or runtime assignment may be intentional; verify the requested pointer route before claiming it works.");
            }
            else if(module is StandaloneInputModule)
            {
                value["inputBackend"]="Legacy Input Manager";
                if(interactiveUgui&&module.isActiveAndEnabled&&!LegacyCompiled)
                    Warning(warnings,"LEGACY_MODULE_BACKEND_MISMATCH",Target(module.gameObject),"Enabled StandaloneInputModule uses legacy input, which is absent from the current compilation. Check backend compatibility; no legacy input was polled by this inspection.");
            }
            else value["inputBackend"]="Custom/other module; configuration not interpreted";
            return value;
        }
        static JObject Raycaster(GraphicRaycaster raycaster)
        {
            return new JObject{["enabled"]=raycaster.enabled,["activeAndEnabled"]=raycaster.isActiveAndEnabled,["ignoreReversedGraphics"]=raycaster.ignoreReversedGraphics,["blockingObjects"]=raycaster.blockingObjects.ToString(),["blockingMask"]=raycaster.blockingMask.value,["eventCamera"]=Name(raycaster.eventCamera)};
        }
        static JObject Graphic(Graphic graphic)=>new JObject{["name"]=Name(graphic),["enabled"]=graphic.enabled,["activeInHierarchy"]=graphic.gameObject.activeInHierarchy,["raycastTarget"]=graphic.raycastTarget};
        static JArray CanvasGroups(Transform transform)
        {
            var groups=new JArray();for(var current=transform;current&&groups.Count<4;current=current.parent)
                foreach(var group in current.GetComponents<CanvasGroup>().Take(4-groups.Count))
                    groups.Add(new JObject{["name"]=Name(group),["enabled"]=group.enabled,["interactable"]=group.interactable,["blocksRaycasts"]=group.blocksRaycasts,["ignoreParentGroups"]=group.ignoreParentGroups,["alpha"]=group.alpha});
            return groups;
        }
        static JObject ButtonFacts(Button button,JArray warnings)
        {
            var canvas=button.GetComponentInParent<Canvas>(true);var graphics=button.GetComponentsInChildren<Graphic>(true);int candidates=graphics.Count(g=>g&&g.isActiveAndEnabled&&g.raycastTarget);
            var callbacks=new JArray();int persistent=button.onClick.GetPersistentEventCount();
            for(int i=0;i<Math.Min(persistent,3);i++)callbacks.Add(new JObject{["targetName"]=Name(button.onClick.GetPersistentTarget(i)),["method"]=Short(button.onClick.GetPersistentMethodName(i)),["state"]=button.onClick.GetPersistentListenerState(i).ToString()});
            if(button.isActiveAndEnabled&&button.interactable&&candidates==0)
                Warning(warnings,"NO_STANDARD_POINTER_GRAPHIC",Target(button.gameObject),"Interactive button has no enabled raycast-target Graphic in its subtree. Keyboard activation or custom forwarding may still work; ordinary pointer delivery needs verification.");
            return new JObject{["target"]=Target(button.gameObject),["name"]=Name(button),["enabled"]=button.enabled,["activeInHierarchy"]=button.gameObject.activeInHierarchy,["interactable"]=button.interactable,["navigation"]=button.navigation.mode.ToString(),["canvas"]=canvas?Target(canvas.gameObject):null,["canvasEnabled"]=canvas&&canvas.isActiveAndEnabled,["targetGraphic"]=button.targetGraphic?Graphic(button.targetGraphic):null,["activeRaycastGraphicCandidates"]=candidates,["graphics"]=new JArray(graphics.Where(g=>g).Take(3).Select(Graphic)),["canvasGroups"]=CanvasGroups(button.transform),["persistentCallbackCount"]=persistent,["persistentCallbacks"]=callbacks,["runtimeCallbackCountKnown"]=false};
        }
        internal static JObject ReviewSceneEvidence(IEnumerable<GameObject> objects)
        {
            var received=(objects??Enumerable.Empty<GameObject>()).Where(g=>g&&g.scene.IsValid()).Distinct().Take(MaximumObjects+1).ToArray();var source=received.Take(MaximumObjects).ToArray();
            var eventSystems=source.Select(g=>g.GetComponent<EventSystem>()).Where(e=>e).ToArray();var canvases=source.Select(g=>g.GetComponent<Canvas>()).Where(c=>c).ToArray();var buttons=source.Select(g=>g.GetComponent<Button>()).Where(b=>b).ToArray();var selectables=source.SelectMany(g=>g.GetComponents<Selectable>()).Where(s=>s).ToArray();
            int interactive=selectables.Count(s=>s.isActiveAndEnabled&&s.interactable),activeEvents=eventSystems.Count(e=>e.isActiveAndEnabled);var warnings=new JArray();var events=new JArray();var canvasFacts=new JArray();var buttonFacts=new JArray();
            if(interactive>0&&activeEvents==0)Warning(warnings,"UGUI_EVENT_SYSTEM_NOT_FOUND",null,"Interactive UGUI controls exist but no active EventSystem was found in the inspected objects. Check standard event routing; custom event forwarding is not evaluated.");
            if(interactive>0&&activeEvents>1)Warning(warnings,"MULTIPLE_ACTIVE_EVENT_SYSTEMS",null,"Multiple active EventSystems were found. This can be intentional for multiplayer; verify which system is responsible for the requested UI.");
            foreach(var system in eventSystems.Take(4))
            {
                var modules=system.GetComponents<BaseInputModule>();var active=modules.Count(m=>m&&m.isActiveAndEnabled);var current=system.currentInputModule;
                if(interactive>0&&system.isActiveAndEnabled&&active==0)Warning(warnings,"UGUI_ENABLED_MODULE_NOT_FOUND",Target(system.gameObject),"Active EventSystem has no active enabled BaseInputModule on its object. A current module is not required in Edit Mode, but runtime input delivery needs an eligible module or explicit custom routing.");
                events.Add(new JObject{["target"]=Target(system.gameObject),["name"]=Name(system),["enabled"]=system.enabled,["activeAndEnabled"]=system.isActiveAndEnabled,["enabledModuleCount"]=active,["moduleCount"]=modules.Length,["modules"]=new JArray(modules.Where(m=>m).Take(2).Select(m=>Module(m,warnings,interactive>0))),["runtimeObservation"]=new JObject{["applicationIsPlaying"]=Application.isPlaying,["currentModuleType"]=current?current.GetType().FullName:null,["interpretation"]=Application.isPlaying?"Observed module state only; event dispatch and pointer delivery are not exercised.":"Current module may legitimately be null before Play Mode/event processing; null is not a structural error."}});
            }
            foreach(var canvas in canvases.Take(6))
            {
                var casters=canvas.GetComponents<GraphicRaycaster>();int active=casters.Count(c=>c&&c.isActiveAndEnabled);var root=canvas.rootCanvas;int ancestorCasters=0;
                for(var parent=canvas.transform.parent;parent;parent=parent.parent)ancestorCasters+=parent.GetComponents<GraphicRaycaster>().Count(c=>c&&c.isActiveAndEnabled);
                int controls=selectables.Count(s=>s.isActiveAndEnabled&&s.interactable&&s.GetComponentInParent<Canvas>(true)==canvas);
                if(controls>0&&active==0&&ancestorCasters==0)Warning(warnings,"UGUI_GRAPHIC_RAYCASTER_NOT_FOUND",Target(canvas.gameObject),"Interactive controls use this Canvas, but no active GraphicRaycaster was found here or on ancestors. Keyboard/custom dispatch is separate; verify pointer raycasting.");
                canvasFacts.Add(new JObject{["target"]=Target(canvas.gameObject),["name"]=Name(canvas),["enabled"]=canvas.enabled,["activeAndEnabled"]=canvas.isActiveAndEnabled,["renderMode"]=canvas.renderMode.ToString(),["rootCanvas"]=root?Target(root.gameObject):null,["assignedCamera"]=Name(canvas.worldCamera),["raycasterCount"]=casters.Length,["activeRaycasterCount"]=active,["activeAncestorRaycasterCount"]=ancestorCasters,["interactiveControlsWithNearestCanvas"]=controls,["raycasters"]=new JArray(casters.Where(c=>c).Take(2).Select(Raycaster))});
            }
            foreach(var button in buttons.Take(8))buttonFacts.Add(ButtonFacts(button,warnings));
            var result=new JObject{["readOnly"]=true,["observedOnlyNotPointerTest"]=true,["inputWasPolled"]=false,["moduleActivationWasPolled"]=false,["actionsEnabledByInspection"]=false,["inputSystemCompiled"]=NewInputCompiled,["legacyInputManagerCompiled"]=LegacyCompiled,["inspectedObjects"]=source.Length,["inputScopeTruncated"]=received.Length>MaximumObjects,["uguiPresent"]=canvases.Length>0||selectables.Length>0,["uiToolkitDocumentCount"]=source.Count(g=>g.GetComponents<Component>().Any(c=>c&&c.GetType().FullName=="UnityEngine.UIElements.UIDocument")),["counts"]=new JObject{["eventSystems"]=eventSystems.Length,["activeEventSystems"]=activeEvents,["canvases"]=canvases.Length,["selectables"]=selectables.Length,["interactiveSelectables"]=interactive,["buttons"]=buttons.Length},["warnings"]=warnings,["eventSystems"]=events,["canvases"]=canvasFacts,["buttons"]=buttonFacts,["callbackCountMeaning"]="Persistent callbacks are serialized data only. Runtime AddListener bindings are not counted; zero persistent callbacks is not an error.",["scopeMeaning"]="UGUI structural evidence only. Other UI frameworks are not required to use UGUI. CanvasGroup/Graphic/custom raycast filters, occlusion and runtime bindings still require actual pointer tests.",["detailLimitCharacters"]=MaximumCharacters,["detailsTruncated"]=false};
            while(result.ToString(Formatting.None).Length>MaximumCharacters)
            {
                result["detailsTruncated"]=true;
                if(buttonFacts.Count>1)buttonFacts.RemoveAt(buttonFacts.Count-1);
                else if(canvasFacts.Count>1)canvasFacts.RemoveAt(canvasFacts.Count-1);
                else if(events.Count>1)events.RemoveAt(events.Count-1);
                else if(warnings.Count>2)warnings.RemoveAt(warnings.Count-1);
                else break;
            }
            return result;
        }
    }
}
