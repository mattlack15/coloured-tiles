using UnityEngine;
using UnityEngine.AI;

namespace Jam
{
    /// <summary>
    /// Drives a NavMeshAgent and handles the two things Unity's solver does not do for us.
    ///
    /// 1. The player is not a NavMeshAgent, so RVO is blind to it. NPCs deliberately do NOT dodge
    ///    the player - a short forward probe stops the agent so it leans on you instead of clipping
    ///    through the collider. That is the "they are in your way" behaviour.
    ///
    /// 2. RVO is bad at head-on symmetry: two agents walking straight at each other, or a knot of
    ///    agents all converging on one lit tile, can deadlock with everyone stopped. When an agent
    ///    detects another agent dead ahead that is either coming at it or already touching, it
    ///    sidesteps along its own right-hand vector. Everyone doing this produces a "keep right"
    ///    traffic rule, which dissolves head-on pairs instead of locking them up.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NpcLocomotion : MonoBehaviour
    {
        public NavMeshAgent Agent { get; private set; }

        [Tooltip("Body radius, kept in sync with the capsule collider by GameBootstrap.")]
        public float BodyRadius = 0.32f;

        public LayerMask PlayerMask;
        public LayerMask NpcMask;

        /// <summary>When false the NPC ignores the player entirely and just crowds into them.</summary>
        public bool PressAgainstPlayer = true;

        [Header("Anti head-on")]
        public bool SidestepEnabled = true;
        [Tooltip("Lateral speed used to slide past an agent that is dead ahead.")]
        public float SidestepSpeed = 1.5f;
        [Tooltip("How far ahead to look for a blocker.")]
        public float HeadOnRange = 1.5f;
        [Tooltip("Cosine of the half-angle of the forward cone that counts as 'dead ahead'.")]
        public float HeadOnDot = 0.45f;

        Vector3 _destination;
        bool _hasDestination;
        bool _halted;
        float _sidestepTimer;

        readonly Collider[] _others = new Collider[24];

        public bool IsHalted => _halted;

        void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
        }

        public void SetTarget(Vector3 world)
        {
            _destination = world;
            _hasDestination = true;
            _halted = false;
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            Agent.isStopped = false;
            Agent.SetDestination(world);
        }

        /// <summary>Give up and stand still (frustrated, or the round is over).</summary>
        public void Halt()
        {
            _halted = true;
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            Agent.isStopped = true;
            Agent.ResetPath();
        }

        void Update()
        {
            if (!Agent.enabled || !Agent.isOnNavMesh) return;

            if (_hasDestination && PressAgainstPlayer) PressOnPlayer();
            FightHeadOn(Time.deltaTime);
        }

        /// <summary>Stop dead when the player's body is directly in front, so the NPC presses into
        /// you rather than walking through the collider.</summary>
        void PressOnPlayer()
        {
            Vector3 dir = Agent.steeringTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            dir.Normalize();

            Vector3 origin = transform.position + Vector3.up * 0.5f;
            float r = BodyRadius * 0.75f;
            bool blocked = Physics.SphereCast(origin, r, dir, out _, r, PlayerMask, QueryTriggerInteraction.Ignore);

            if (blocked && !_halted)
            {
                _halted = true;
                Agent.isStopped = true;
            }
            else if (!blocked && _halted)
            {
                _halted = false;
                Agent.isStopped = false;
                Agent.SetDestination(_destination);
            }
        }

        /// <summary>
        /// Head-on resolution. Only fires for other NPCs (they carry a Rigidbody); the player is
        /// deliberately excluded so blocking you stays their job.
        /// </summary>
        void FightHeadOn(float dt)
        {
            if (!SidestepEnabled)
            {
                _sidestepTimer = 0f;
                return;
            }

            Vector3 heading = Agent.velocity;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.04f)
            {
                heading = Agent.steeringTarget - transform.position;
                heading.y = 0f;
            }
            if (heading.sqrMagnitude < 0.0004f) return;
            heading.Normalize();

            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, HeadOnRange,
                                                  _others, NpcMask, QueryTriggerInteraction.Ignore);

            bool jammed = false;
            for (int i = 0; i < n; i++)
            {
                var rb = _others[i].attachedRigidbody;
                if (rb == null || rb.transform == transform) continue;

                Vector3 to = rb.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.001f) continue;

                Vector3 dir = to / dist;
                if (Vector3.Dot(dir, heading) < HeadOnDot) continue;   // not in front of me

                var other = rb.GetComponent<NavMeshAgent>();
                Vector3 otherVelocity = other != null ? other.velocity : Vector3.zero;
                otherVelocity.y = 0f;

                // Either it is coming at me, or it is simply parked in my lane.
                bool approaching = otherVelocity.sqrMagnitude > 0.04f && Vector3.Dot(otherVelocity, dir) < 0.1f;
                bool touching = dist < BodyRadius * 2.1f;

                if (approaching || touching) { jammed = true; break; }
            }

            if (!jammed)
            {
                _sidestepTimer = Mathf.Max(0f, _sidestepTimer - dt * 3f);
                return;
            }

            _sidestepTimer += dt;
            float ramp = Mathf.Clamp01(_sidestepTimer / 0.12f);

            // Right-hand rule: everyone yields to the same side, so paired agents separate instead
            // of mirroring each other back into a deadlock.
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            if (side.sqrMagnitude < 0.0001f) return;
            side.Normalize();

            Agent.Move(side * (SidestepSpeed * ramp * dt));
        }
    }
}
