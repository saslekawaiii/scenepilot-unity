using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace WorldWeaver.Editor.Assistant
{
    internal static class UiConstruction
    {
        internal static void Validate(EditorCommand c,GameObject parent,Func<string,GameObject> resolve)
        {
            string kind=(string)c.args["kind"];
            if(kind!="canvas"&&parent&&!parent.GetComponentInParent<Canvas>())throw new ArgumentException("UI parent must be an owned Canvas/RectTransform; create a Canvas first.");
            if(kind=="canvas"&&parent&&parent.GetComponentInParent<Canvas>())throw new ArgumentException("Do not nest overlay Canvases.");
            if(c.args["toggle_target"]!=null)
            {if(kind!="button"||!resolve((string)c.args["toggle_target"]).GetComponent<GeneratedSceneObject>())throw new ArgumentException("Button toggle requires an owned explicit target.");}
        }
        internal static GameObject Create(EditorCommand c,SceneBuildTransaction tx,Transform parent,Func<string,GameObject> resolve)
        {
            var a=c.args;string kind=(string)a["kind"];
            if(kind!="canvas"&&(!parent||!parent.GetComponentInParent<Canvas>()))
            {var canvas=Canvas(tx,parent);parent=canvas.transform;}
            var go=tx.Empty(c.name,parent,kind=="canvas"?"ui_canvas":"ui");
            var rect=Undo.AddComponent<RectTransform>(go);
            rect.sizeDelta=a["size"] is JArray size?new Vector2((float)size[0],(float)size[1]):new Vector2(240,kind=="text"?50:64);
            rect.anchoredPosition=a["position"] is JArray pos?new Vector2((float)pos[0],(float)pos[1]):Vector2.zero;
            if(kind=="canvas")SetupCanvas(tx,go);
            else if(kind=="text")Text(tx,go,(string)a["text"]??c.name,ConstructiveTools.C(a["color"]));
            else
            {
                var image=Undo.AddComponent<Image>(go);image.color=a["color"]!=null?ConstructiveTools.C(a["color"]):new Color(.13f,.17f,.23f,.97f);
                if(kind=="button")
                {
                    var button=Undo.AddComponent<Button>(go);button.targetGraphic=image;
                    var label=tx.Empty("Label",go.transform,"ui");Stretch(Undo.AddComponent<RectTransform>(label));Text(tx,label,(string)a["text"]??c.name,Color.white);
                    if(a["toggle_target"]!=null){var action=Undo.AddComponent<WorldUIButton>(go);action.Target=resolve((string)a["toggle_target"]);UnityEventTools.AddPersistentListener(button.onClick,action.Toggle);}
                }
                if(kind=="slider")
                {
                    var fill=tx.Empty("Fill",go.transform,"ui");var fillRect=Undo.AddComponent<RectTransform>(fill);Stretch(fillRect);Undo.AddComponent<Image>(fill).color=new Color(.25f,.75f,.65f);
                    var handle=tx.Empty("Handle",go.transform,"ui");var handleRect=Undo.AddComponent<RectTransform>(handle);handleRect.sizeDelta=new Vector2(18,rect.sizeDelta.y);var handleImage=Undo.AddComponent<Image>(handle);handleImage.color=Color.white;
                    var slider=Undo.AddComponent<Slider>(go);slider.fillRect=fillRect;slider.handleRect=handleRect;slider.targetGraphic=handleImage;slider.value=.5f;
                }
            }
            return go;
        }
        static GameObject Canvas(SceneBuildTransaction tx,Transform parent){var go=tx.Empty("Интерфейс",parent,"ui_canvas");Undo.AddComponent<RectTransform>(go);SetupCanvas(tx,go);return go;}
        static void SetupCanvas(SceneBuildTransaction tx,GameObject go)
        {
            var canvas=Undo.AddComponent<Canvas>(go);canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=Undo.AddComponent<CanvasScaler>(go);scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
            Undo.AddComponent<GraphicRaycaster>(go);
            if(!UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Any(e=>e.gameObject.scene==go.scene))
            {
                var events=tx.Empty("UI EventSystem",go.transform.parent,"ui_events");Undo.AddComponent<EventSystem>(events);
#if ENABLE_INPUT_SYSTEM
                Undo.AddComponent<InputSystemUIInputModule>(events).AssignDefaultActions();
#else
                Undo.AddComponent<StandaloneInputModule>(events);
#endif
            }
        }
        static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void Text(SceneBuildTransaction tx,GameObject go,string value,Color color)
        {var text=Undo.AddComponent<Text>(go);text.text=value;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=22;text.alignment=TextAnchor.MiddleCenter;text.color=color;text.raycastTarget=false;}
    }
}
