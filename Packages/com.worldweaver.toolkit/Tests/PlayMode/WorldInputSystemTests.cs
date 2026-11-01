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
