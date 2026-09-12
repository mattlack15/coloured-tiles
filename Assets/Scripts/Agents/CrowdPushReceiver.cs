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
        [Tooltip("How hard this body resists, relative to the crowd's 0.85 to 1.30. An overlap is split by this ratio, so 2.2 means a bot gives up roughly two thirds of the ground while you give one third - both move, you just move less.")]
        public float PushForce = 0.45f;

        [Tooltip("Ceiling on how far a shove can move the body in one frame. At 60fps 0.05 is about 3 m/s.")]
        public float MaxShovePerFrame = 0.05f;

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

            Vector3 displacement = Vector3.zero;

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

                float contact = _cc.radius + npc.BodyRadius;
                if (dist >= contact) continue;

                // Split the overlap by relative force. Both bodies give ground and the weaker one
                // gives more, so a bot does push the player - just less than the player pushes it.
                // Requiring the pack to out-total the player instead made one bot do nothing at all,
                // which is immunity dressed up as weakness.
                float share = npc.PushForce / Mathf.Max(0.01f, npc.PushForce + PushForce);
                displacement += away / dist * ((contact - dist) * share);
            }

            if (displacement.sqrMagnitude < 0.000001f) return;

            // Capped per frame so a dense pack jostles rather than flings.
            displacement = Vector3.ClampMagnitude(displacement, MaxShovePerFrame);
            displacement.y = 0f;                                  // horizontal only
            _cc.Move(displacement);
        }
    }
}
