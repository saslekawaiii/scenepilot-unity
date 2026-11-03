using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace WorldWeaver
{
    /// <summary>Read the active Unity input backend without forcing a project's input settings to change.</summary>
    public static class WorldInput
    {
        public static string Backend =>
#if ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
            "Input System and legacy Input Manager; WorldInput prefers Input System";
#elif ENABLE_INPUT_SYSTEM
            "Input System only; unconditional UnityEngine.Input calls fail at runtime";
#elif ENABLE_LEGACY_INPUT_MANAGER
            "Legacy Input Manager";
#else
            "No active input backend";
#endif
#if ENABLE_INPUT_SYSTEM
        static Keyboard imeKeyboard;
        static bool composing;
        static Keyboard KeyboardDevice
        {
            get
            {
                var current=Keyboard.current;
                if(current==imeKeyboard)return current;
                if(imeKeyboard!=null)imeKeyboard.onIMECompositionChange-=CompositionChanged;
                imeKeyboard=current;composing=false;
                if(imeKeyboard!=null)imeKeyboard.onIMECompositionChange+=CompositionChanged;
                return current;
            }
        }
        static void CompositionChanged(IMECompositionString text)=>composing=text.Count>0;
#endif
        public static Vector2 Movement
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k=KeyboardDevice;if(k==null)return Vector2.zero;
                return Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0),
                    (k.wKey.isPressed||k.upArrowKey.isPressed?1:0)-(k.sKey.isPressed||k.downArrowKey.isPressed?1:0)),1);
#elif ENABLE_LEGACY_INPUT_MANAGER
                return new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
#else
                return Vector2.zero;
#endif
            }
        }
        public static float TurnDegrees(float speed,float deltaTime)
            =>LookDegrees(speed,deltaTime,true).x;
        public static Vector2 LookDegrees(float speed,float deltaTime,bool requireRightButton=true)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse=Mouse.current;return mouse!=null&&(!requireRightButton||mouse.rightButton.isPressed)?mouse.delta.ReadValue()*speed*.0025f:Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return !requireRightButton||Input.GetMouseButton(1)?new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"))*speed*deltaTime:Vector2.zero;
#else
            return Vector2.zero;
#endif
        }
        public static bool SprintHeld
        {
            get {
#if ENABLE_INPUT_SYSTEM
                var k=KeyboardDevice;return k!=null&&(k.leftShiftKey.isPressed||k.rightShiftKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
#else
                return false;
#endif
            }
        }
        public static bool JumpPressed=>KeyState(KeyCode.Space,true);
        public static bool InteractPressed=>KeyState(KeyCode.E,true);
        public static bool InteractHeld=>KeyState(KeyCode.E,false);
        public static bool CancelPressed=>KeyState(KeyCode.Escape,true);
        static bool KeyState(KeyCode key,bool pressed)
        {
#if ENABLE_INPUT_SYSTEM
            var k=KeyboardDevice;if(k==null)return false;
            var control=key==KeyCode.E?k.eKey:key==KeyCode.Space?k.spaceKey:k.escapeKey;
            return pressed?control.wasPressedThisFrame:control.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return pressed?Input.GetKeyDown(key):Input.GetKey(key);
#else
            return false;
#endif
        }
        public static bool IsComposing
        {
            get {
#if ENABLE_INPUT_SYSTEM
                _=KeyboardDevice;return composing;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return !string.IsNullOrEmpty(Input.compositionString);
#else
                return false;
#endif
            }
        }
    }
}
