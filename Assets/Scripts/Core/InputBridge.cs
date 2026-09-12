using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam
{
    /// <summary>
    /// One place that reads movement, jump and punch - from keyboard/mouse or from a gamepad.
    ///
    /// The project runs with activeInputHandler = Both, so the legacy Input class and the new Input
    /// System are both live. Everything here reads the NEW one, because it is the only way to see a
    /// controller's sticks and buttons by name. The legacy KeyCode checks the player used before
    /// (Input.GetKey(KeyCode.D) and friends) cannot respond to a gamepad at all: KeyCode is
    /// keyboard-scancode based, so a controller simply does not exist as far as that code is
    /// concerned.
    ///
    /// Kept as plain statics rather than an input asset so it needs no wiring and works the moment a
    /// pad is plugged in.
    /// </summary>
    public static class InputBridge
    {
        /// <summary>Left stick, WASD or the arrow keys. Normalised when it exceeds unit length.</summary>
        public static Vector2 Move
        {
            get
            {
                Vector2 v = Vector2.zero;

                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                }

                var pad = Gamepad.current;
                if (pad != null)
                {
                    Vector2 stick = pad.leftStick.ReadValue();
                    if (stick.sqrMagnitude > v.sqrMagnitude) v = stick;
                }

                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        /// <summary>Punch: E, J, left mouse, or the pad's left face button / right trigger.</summary>
        public static bool PunchPressed
        {
            get
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.eKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame)) return true;

                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

                var pad = Gamepad.current;
                if (pad == null) return false;
                return pad.buttonWest.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame;
            }
        }

        public static bool RestartPressed
        {
            get
            {
                var kb = Keyboard.current;
                if (kb != null && kb.rKey.wasPressedThisFrame) return true;

                var pad = Gamepad.current;
                return pad != null && pad.startButton.wasPressedThisFrame;
            }
        }

        /// <summary>
        /// Where the player is pointing: the right stick if it is being pushed, otherwise wherever
        /// they are walking. That gives a pad a proper twin-stick aim while leaving keyboard players
        /// aiming in the direction they move.
        /// </summary>
        public static Vector2 Aim
        {
            get
            {
                var pad = Gamepad.current;
                if (pad != null)
                {
                    Vector2 stick = pad.rightStick.ReadValue();
                    if (stick.sqrMagnitude > 0.2f) return stick.normalized;
                }
                return Move;
            }
        }

        public static bool HasGamepad => Gamepad.current != null;
    }
}
