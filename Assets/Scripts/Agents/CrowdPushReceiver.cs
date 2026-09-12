using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Lets a crowd of NPCs physically shove this body.
    ///
    /// Deliberately its own component, and deliberately NOT physics. The crowd's bodies are
    /// kinematic, so letting PhysX resolve a contact against a CharacterController means the
    /// controller has to give way - and penetration recovery picks the shortest exit vector, which
    /// is sometimes straight up. That is what used to leave the player hovering off the floor.
    /// Moving the body by hand, horizontally and speed-capped, means a shove can never lift anyone.
    ///
    /// Force is compared per contact, so a body that out-forces every individual agent cannot be
    /// moved by one - it takes a pack adding up to more than you. That is what makes the crowd
    /// dangerous in numbers while a single agent is only an obstacle.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CrowdPushReceiver : MonoBehaviour
    {
        [Tooltip("How hard this body resists. NPCs run 0.85 to 1.30, so 1.6 means no single agent can move you, but several together can.")]
        public float PushForce = 1.6f;

        [Tooltip("Metres per second of shove per unit of combined force advantage.")]
        public float ShoveSpeedPerForce = 2f;

        [Tooltip("Ceiling on shove speed, so a dense pack cannot fling the body.")]
        public float MaxShoveSpeed = 3f;

        public LayerMask NpcMask;

        CharacterController _cc;
        readonly Collider[] _others = new Collider[32];

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        void LateUpdate()
        {
            if (NpcMask == 0 || _cc == null) return;

            float reach = _cc.radius + 0.35f;
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, reach,
                                                  _others, NpcMask, QueryTriggerInteraction.Ignore);

            Vector3 direction = Vector3.zero;
            float incoming = 0f;

            for (int i = 0; i < n; i++)
            {
                var rb = _others[i].attachedRigidbody;
                if (rb == null || rb.transform == transform) continue;

                var npc = rb.GetComponent<NpcLocomotion>();
                if (npc == null) continue;

                Vector3 away = transform.position - rb.transform.position;
                away.y = 0f;
                float dist = away.magnitude;
                if (dist < 0.001f) continue;
                if (dist >= _cc.radius + npc.BodyRadius) continue;

                direction += away / dist;
                incoming += npc.PushForce;
            }

            // Compare the PACK against this body, not each agent against it. Checking one at a time
            // would mean a body stronger than any individual could never be moved at all, however
            // many were leaning on it - which is the opposite of the intent.
            if (incoming <= PushForce) return;
            if (direction.sqrMagnitude < 0.0001f) return;

            float excess = incoming - PushForce;
            Vector3 velocity = direction.normalized * Mathf.Min(excess * ShoveSpeedPerForce, MaxShoveSpeed);
            velocity.y = 0f;                                      // horizontal only
            _cc.Move(velocity * Time.deltaTime);
        }
    }
}
