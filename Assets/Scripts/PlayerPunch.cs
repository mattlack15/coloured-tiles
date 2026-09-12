using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam
{
    /// <summary>
    /// Player punch, ported from the playerLogic branch.
    ///
    /// Kept from the original: a hitbox parked a fixed distance ahead along the aim direction,
    /// armed for a 0.2s window by a punch press, with the hit itself doing knockback.
    ///
    /// Changed for this project: the original read a punchAction InputActionReference and aimed with
    /// a right stick through a LineRenderer. This project's input asset has no punch action and
    /// there is no gamepad aim, so the press is read from the keyboard and the aim is the last
    /// direction the player moved - the same "remember the last aim direction" idea, without needing
    /// a second stick to drive it.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerPunch : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("How far ahead of the player the hitbox sits when it fires, mirroring how the original parked it along the aim line.")]
        public float Reach = 1.1f;

        [Header("Wiring")]
        public PunchHitbox Hitbox;
        public LayerMask TargetMask;

        const float ArmSeconds = 0.2f;

        float _armedUntil;
        Vector3 _aim = Vector3.forward;

        public bool IsArmed => _armedUntil > 0f;

        void Awake()
        {
            if (Hitbox == null) Hitbox = GetComponentInChildren<PunchHitbox>(true);

            if (Hitbox != null)
            {
                Hitbox.TargetMask = TargetMask;
                Hitbox.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (Hitbox == null) return;

            // Keyboard aims where you walk; a pad aims with the right stick. See InputBridge.
            Vector2 aim = InputBridge.Aim;
            if (aim.sqrMagnitude > 0.01f)
                _aim = new Vector3(aim.x, 0f, aim.y).normalized;

            if (InputBridge.PunchPressed)
                Fire();

            if (_armedUntil > 0f && Time.time >= _armedUntil)
            {
                Hitbox.gameObject.SetActive(false);
                _armedUntil = 0f;
            }
        }

        void Fire()
        {
            Vector3 origin = transform.position;
            Vector3 at = origin + _aim * Reach;
            at.y = origin.y;

            Hitbox.transform.position = at;
            Hitbox.transform.rotation = Quaternion.LookRotation(_aim, Vector3.up);

            Hitbox.gameObject.SetActive(true);
            Hitbox.Fire();                       // lands once per press, not every frame it is armed
            _armedUntil = Time.time + ArmSeconds;
        }
    }
}
