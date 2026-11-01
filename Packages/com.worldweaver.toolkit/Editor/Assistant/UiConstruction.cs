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
            string kind=c.tool=="configure_ui"?(parent&&parent.GetComponent<Button>()?"button":parent&&parent.GetComponent<Canvas>()?"canvas":"element"):(string)c.args["kind"];
            if(kind!="canvas"&&parent&&!parent.GetComponentInParent<Canvas>())throw new ArgumentException("UI parent must be an owned Canvas/RectTransform; create a Canvas first.");
            if(c.tool=="create_ui_element"&&kind=="canvas"&&parent&&parent.GetComponentInParent<Canvas>())throw new ArgumentException("Do not nest overlay Canvases.");
            if(c.tool=="configure_ui")
            {
                if(!parent||!parent.GetComponent<RectTransform>())throw new ArgumentException("Target must be a UI RectTransform.");
                if(c.args["color"]!=null&&!parent.GetComponent<Graphic>())throw new ArgumentException("A bare Canvas has no color; choose its panel or text.");
                if(c.args["text"]!=null&&!parent.GetComponent<Text>()&&!parent.GetComponentInChildren<Text>(true))throw new ArgumentException("Choose a Text or existing button/panel label before editing text.");
            }
            if(c.args["toggle_target"]!=null)
            {if(kind!="button"||!resolve((string)c.args["toggle_target"]).GetComponent<GeneratedSceneObject>())throw new ArgumentException("Button toggle requires an owned explicit target.");}
        }
        internal static GameObject Create(EditorCommand c,SceneBuildTransaction tx,Transform parent,Func<string,GameObject> resolve)
        {
            var a=c.args;string kind=(string)a["kind"];
            if(kind!="canvas"&&(!parent||!parent.GetComponentInParent<Canvas>()))
            {var candidates=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(x=>x.gameObject.scene==UnityEngine.SceneManagement.SceneManager.GetActiveScene()&&x.GetComponent<GeneratedSceneObject>()).ToArray();
                if(candidates.Length>1)throw new ArgumentException("Several owned Canvases exist; choose an explicit UI parent.");
                var canvas=candidates.Length==1?candidates[0].gameObject:Canvas(tx,parent);parent=canvas.transform;}
            var go=tx.Empty(c.name,parent,kind=="canvas"?"ui_canvas":"ui");
            var rect=Undo.AddComponent<RectTransform>(go);
            rect.sizeDelta=a["size"] is JArray size?new Vector2((float)size[0],(float)size[1]):kind=="panel"?new Vector2(560,360):new Vector2(240,kind=="text"?50:64);
            rect.anchoredPosition=a["position"] is JArray pos?new Vector2((float)pos[0],(float)pos[1]):Vector2.zero;
            if(kind=="canvas")SetupCanvas(tx,go);
            else if(kind=="text")Text(tx,go,(string)a["text"]??c.name,ConstructiveTools.C(a["color"]));
            else
            {
                var image=Undo.AddComponent<Image>(go);image.color=a["color"]!=null?ConstructiveTools.C(a["color"]):new Color(.13f,.17f,.23f,.97f);
                if(kind=="panel"&&a["text"]!=null)
                {
                    var label=tx.Empty("Title",go.transform,"ui");var titleRect=Undo.AddComponent<RectTransform>(label);titleRect.anchorMin=new Vector2(0,1);titleRect.anchorMax=Vector2.one;titleRect.pivot=new Vector2(.5f,1);titleRect.sizeDelta=new Vector2(-32,48);titleRect.anchoredPosition=new Vector2(0,-12);Text(tx,label,(string)a["text"],Color.white);
                }
                if(kind=="button")
                {
                    var button=Undo.AddComponent<Button>(go);button.targetGraphic=image;
                    var label=tx.Empty("Label",go.transform,"ui");Stretch(Undo.AddComponent<RectTransform>(label));Text(tx,label,(string)a["text"]??c.name,Color.white);
                    if(a["toggle_target"]!=null)Bind(go,resolve((string)a["toggle_target"]));
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
        internal static void Configure(EditorCommand c,SceneBuildTransaction tx,GameObject go,Func<string,GameObject> resolve)
        {
            var a=c.args;var rect=go.GetComponent<RectTransform>();if(!rect)throw new ArgumentException("Target must be a UI RectTransform.");
            Undo.RecordObject(rect,"Configure UI layout");
            if(a["position"] is JArray pos)rect.anchoredPosition=new Vector2((float)pos[0],(float)pos[1]);
            if(a["size"] is JArray size)rect.sizeDelta=new Vector2((float)size[0],(float)size[1]);
            if(a["color"]!=null){var graphic=go.GetComponent<Graphic>();if(!graphic)throw new ArgumentException("Choose a Text/Image/Button for a color, not a bare Canvas.");Undo.RecordObject(graphic,"Configure UI color");graphic.color=ConstructiveTools.C(a["color"]);}
            if(a["text"]!=null)
            {
                var text=go.GetComponent<UnityEngine.UI.Text>();
                if(!text&&(go.GetComponent<Button>()||go.GetComponent<Image>()))text=go.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if(!text)throw new ArgumentException("Text updates require an existing Text or button label. Create a separate text element for a panel heading.");
                Undo.RecordObject(text,"Configure UI text");text.text=(string)a["text"];
            }
            if(a["toggle_target"]!=null)Bind(go,resolve((string)a["toggle_target"]));
            if(a["visible"]!=null){Undo.RecordObject(go,"Configure UI visibility");go.SetActive((bool)a["visible"]);}
        }
        static void Bind(GameObject go,GameObject target)
        {
            var button=go.GetComponent<Button>();if(!button)throw new ArgumentException("A toggle action requires a Button.");
            var action=go.GetComponent<WorldUIButton>();if(!action)action=Undo.AddComponent<WorldUIButton>(go);
            Undo.RecordObject(action,"Bind UI target");action.Target=target;Undo.RecordObject(button,"Bind UI click");
            bool present=Enumerable.Range(0,button.onClick.GetPersistentEventCount()).Any(i=>button.onClick.GetPersistentTarget(i)==action&&button.onClick.GetPersistentMethodName(i)==nameof(WorldUIButton.Toggle));
            if(!present)UnityEventTools.AddPersistentListener(button.onClick,action.Toggle);
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
