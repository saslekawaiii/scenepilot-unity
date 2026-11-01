using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
#endif

namespace WorldWeaver.Tests
{
    public sealed class ConstructivePlayTests
    {
        [UnityTest] public IEnumerator ResourceIsFiniteAndRequiresActualProximity()
        {
            var root=new GameObject("Disposable survival");var environment=root.AddComponent<WorldEnvironment>();
            var player=new GameObject("Player");player.transform.SetParent(root.transform);player.AddComponent<CharacterController>();var motor=player.AddComponent<WorldPlayer>();motor.enabled=false;
            var survival=player.AddComponent<WorldSurvival>();survival.Environment=environment;survival.State.Oxygen=10;
            var node=new GameObject("Reserve");node.transform.SetParent(root.transform);var resource=node.AddComponent<WorldResourceNode>();
            try
            {
                yield return null;node.transform.position=Vector3.right*10;Assert.IsFalse(resource.Collect(survival));
                node.transform.position=player.transform.position+Vector3.right;Assert.IsTrue(resource.Collect(survival));float oxygen=survival.State.Oxygen;Assert.IsFalse(resource.Collect(survival));Assert.AreEqual(oxygen,survival.State.Oxygen);
                survival.Respawn();Assert.IsFalse(resource.Collected);Assert.AreEqual(100,survival.State.Health);Assert.AreEqual(100,survival.State.Oxygen);
            }
            finally{Object.Destroy(root);}
        }
        [UnityTest] public IEnumerator RigidbodyFallsAndBouncesWithActualPhysics()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(12,1,12);
            var ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);ball.transform.position=new Vector3(0,3,0);var body=ball.AddComponent<Rigidbody>();body.mass=2;
            var material=new PhysicsMaterial{bounciness=.8f,bounceCombine=PhysicsMaterialCombine.Maximum,dynamicFriction=.1f,staticFriction=.1f};ball.GetComponent<Collider>().sharedMaterial=material;
            Vector3 gravity=Physics.gravity;Physics.gravity=new Vector3(0,-9.81f,0);
            try
            {
                bool fell=false,bounced=false;float deadline=Time.time+4;
                while(Time.time<deadline&&!bounced){yield return new WaitForFixedUpdate();if(body.linearVelocity.y<-2)fell=true;if(fell&&body.linearVelocity.y>2&&ball.transform.position.y<1.2f)bounced=true;}
                Assert.IsTrue(fell,"Actual downward velocity");Assert.IsTrue(bounced,"Actual upward velocity after collision");
            }
            finally{Physics.gravity=gravity;Object.Destroy(ball);Object.Destroy(floor);Object.Destroy(material);}
        }
#if ENABLE_INPUT_SYSTEM
        [UnityTest] public IEnumerator ButtonRespondsToVirtualMouseThroughNormalEventSystem()
        {
            var root=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var buttonGo=new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button));buttonGo.transform.SetParent(root.transform,false);buttonGo.GetComponent<RectTransform>().sizeDelta=new Vector2(200,80);var button=buttonGo.GetComponent<Button>();button.targetGraphic=buttonGo.GetComponent<Image>();
            var target=new GameObject("Toggle target");var action=buttonGo.AddComponent<WorldUIButton>();action.Target=target;button.onClick.AddListener(action.Toggle);
            var events=new GameObject("Events",typeof(EventSystem),typeof(InputSystemUIInputModule));events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var background=InputSystem.settings.backgroundBehavior;var routing=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse=InputSystem.AddDevice<Mouse>();
            try
            {
                mouse.MakeCurrent();var position=new Vector2(Screen.width/2f,Screen.height/2f);InputSystem.QueueStateEvent(mouse,new MouseState{position=position});for(int frame=0;frame<10;frame++)yield return null;
                Canvas.ForceUpdateCanvases();
                var hits=new List<RaycastResult>();events.GetComponent<EventSystem>().RaycastAll(new PointerEventData(events.GetComponent<EventSystem>()){position=position},hits);
                var module=events.GetComponent<InputSystemUIInputModule>();
                string diagnostic=$"mouseEnabled={mouse.enabled}, updateMode={InputSystem.settings.updateMode}, screen={Screen.width}x{Screen.height}, pixelRect={root.GetComponent<Canvas>().pixelRect}, depth={buttonGo.GetComponent<Image>().depth}, hits={string.Join(",",hits.Select(h=>h.gameObject.name))}, pointEnabled={module.point.action.enabled}, clickEnabled={module.leftClick.action.enabled}, pointer={module.point.action.ReadValue<Vector2>()}, mouse={mouse.position.ReadValue()}, module={EventSystem.current?.currentInputModule?.GetType().Name}";
                Assert.IsTrue(hits.Any(h=>h.gameObject==buttonGo),"Actual UI raycast must reach the button. "+diagnostic);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=position}.WithButton(MouseButton.Left));yield return null;yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=position});yield return null;yield return null;
                Assert.IsFalse(target.activeSelf,"Click must traverse InputSystemUIInputModule and Button.onClick, not invoke Toggle in the test. "+diagnostic);
            }
            finally{InputSystem.RemoveDevice(mouse);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;Object.Destroy(events);Object.Destroy(root);Object.Destroy(target);}
        }
#endif
    }
}
