#if ENABLE_INPUT_SYSTEM
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorldWeaver.Tests
{
    /// <summary>Simulated Unity devices in an isolated test, not desktop keyboard automation.</summary>
    public sealed class WorldInputSystemTests
    {
        [UnityTest] public IEnumerator PlayerMouseLookHandlesBothAxesAndButtonPolicyThroughRealInput()
        {
            var background=InputSystem.settings.backgroundBehavior;
            var routing=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse=InputSystem.AddDevice<Mouse>();var root=new GameObject("Mouse look acceptance");
            try
            {
                var camera=new GameObject("View").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);
                var player=root.AddComponent<WorldPlayer>();player.View=camera;player.Speed=0;mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
                Quaternion start=root.transform.rotation;
                InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(30,20)});yield return null;
                Assert.Less(Quaternion.Angle(start,root.transform.rotation),.1f,"RMB policy should ignore unheld mouse");
                player.AlwaysLook=true;
                InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(30,20)});yield return null;
                Assert.Greater(Mathf.DeltaAngle(0,root.transform.eulerAngles.y),5,"Horizontal mouse must turn actor/camera");
                Assert.Less(Mathf.DeltaAngle(0,camera.transform.localEulerAngles.x),-3,"Vertical mouse must change camera pitch");
                InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(-30,-20)});yield return null;
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0,root.transform.eulerAngles.y)),.5f,"Mouse must turn both ways");
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0,camera.transform.localEulerAngles.x)),.5f,"Pitch must reverse");
                InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(0,10000)});yield return null;
                Assert.AreEqual(player.MinPitch,Mathf.DeltaAngle(0,camera.transform.localEulerAngles.x),.1f,"Pitch clamp");
                player.ExternalCameraControl=true;root.transform.rotation=Quaternion.Euler(0,42,0);Quaternion before=camera.transform.rotation;
                InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(100,100)});yield return null;
                Assert.Less(Quaternion.Angle(before,camera.transform.rotation),.1f,"Custom camera must have a single rotation owner");
                Assert.AreEqual(42,root.transform.eulerAngles.y,.1f,"External actor yaw must not be overwritten by the movement controller");
            }
            finally{Object.DestroyImmediate(root);InputSystem.RemoveDevice(mouse);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;}
        }
        [UnityTest] public IEnumerator NewBackendReadsMovementAndGameplayKeysWithoutLegacyInputCalls()
        {
            var background=InputSystem.settings.backgroundBehavior;
            var routing=InputSystem.settings.editorInputBehaviorInPlayMode;
            // This batch fixture has no focused Game View. Route only its isolated virtual-device test to the player loop.
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                // Initialize the newly added virtual keyboard in a released update, like the normal frame before a player's press.
                keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Assert.IsFalse(WorldInput.JumpPressed);Assert.IsFalse(WorldInput.InteractPressed);Assert.IsFalse(WorldInput.CancelPressed);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift,Key.Space,Key.E));yield return null;
                Assert.AreEqual(Vector2.up,WorldInput.Movement);
                Assert.IsTrue(WorldInput.SprintHeld,"Shift held");
                Assert.IsTrue(WorldInput.JumpPressed,"Space press edge");
                Assert.IsTrue(WorldInput.InteractPressed,"E press edge");
                Assert.IsTrue(WorldInput.InteractHeld,"E held");
                Assert.IsFalse(WorldInput.CancelPressed);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;
                Assert.AreEqual(Vector2.zero,WorldInput.Movement);Assert.IsFalse(WorldInput.InteractHeld);Assert.IsTrue(WorldInput.CancelPressed);Assert.IsFalse(WorldInput.IsComposing);
            }
            finally{InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;}
        }
    }
}
#endif
