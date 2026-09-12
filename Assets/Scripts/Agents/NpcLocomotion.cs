using UnityEngine;
using UnityEngine.AI;

namespace Jam
{
    /// <summary>
    /// Drives a NavMeshAgent and handles the things Unity's solver does not do for us.
    ///
    /// 1. The player is not a NavMeshAgent, so RVO is blind to it. Contact with the player is
    ///    resolved here instead:
    ///
    ///      * Player advancing on me  -> I give ground and slide out of the way. The player has the
    ///        force advantage, so you can bulldoze through a crowd.
    ///      * Player not advancing    -> I stand my ground and lean on them.
    ///
    ///    This split matters. Without it the crowd either walks through you or walls you in
    ///    permanently, and because an NPC body is kinematic, a hard standoff gets resolved by
    ///    lifting YOUR capsule - which is what made the player float.
    ///
    /// 2. RVO is bad at head-on symmetry: two agents walking straight at each other, or a knot of
    ///    agents converging on one lit tile, can deadlock with everyone stopped. An agent that
    ///    detects another agent dead ahead sidesteps along its own right-hand vector, which
    ///    produces a "keep right" traffic rule and dissolves head-on pairs.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NpcLocomotion : MonoBehaviour
    {
        public NavMeshAgent Agent { get; private set; }

        [Tooltip("Body radius, kept in sync with the capsule collider by GameBootstrap.")]
        public float BodyRadius = 0.32f;

        public LayerMask PlayerMask;
        public LayerMask NpcMask;

        [Header("Player contact")]
        /// <summary>Legacy switch: when false the NPC ignores the player entirely and only crowds.</summary>
        public bool PressAgainstPlayer = true;

        [Tooltip("The player's force advantage. How fast an NPC slides aside when walked into.")]
        public float PlayerPushSpeed = 2.2f;

        [Tooltip("Player speed (m/s) at which the crowd starts giving ground.")]
        public float PlayerPushMinSpeed = 0.5f;

        [Tooltip("The player's push force, kept in sync with CrowdPushReceiver.PushForce. Used to split an overlap between the two bodies by relative strength.")]
        public float PlayerForce = 2.2f;

        [Header("Shoving")]
        [Tooltip("How hard this agent shoves. Compared with a neighbour's force, and only the weaker body moves, so a contact resolves once instead of both pushing apart.")]
        public float PushForce = 1f;
        [Tooltip("Extra metres per second of shove per unit of force advantage. This is what lets a shove carry someone off their tile rather than merely un-overlapping them.")]
        public float ShoveSpeedPerForce = 2.5f;

        /// <summary>The player's body, read for velocity so NPCs know which way you are bearing down.</summary>
        public CharacterController PlayerBody;

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
        bool _shovedThisFrame;
        float _sidestepTimer;

        /// <summary>A punch's remaining displacement per second. Decays to nothing in a fraction of
        /// a second, so a hit reads as a knockback rather than a teleport.</summary>
        Vector3 _punchVelocity;

        [Tooltip("How fast a punch's knockback bleeds off, in m/s per second. A speed of 11 decaying at 16 carries a body about 3.8 metres.")]
        public float PunchDecay = 16f;

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

        /// <summary>
        /// Stand still and STAY still. Plain <see cref="Halt"/> only lasts a frame: the player
        /// contact check resumes any agent that still has a destination, so an agent told to hold a
        /// tile would wander off again immediately. Clearing the destination is what makes the hold
        /// stick, and <see cref="SetTarget"/> puts it back.
        /// </summary>
        public void StopAndStay()
        {
            _destination = transform.position;
            _hasDestination = false;
            Halt();
        }

        void Update()
        {
            if (!Agent.enabled || !Agent.isOnNavMesh) return;

            float dt = Time.deltaTime;
            _shovedThisFrame = false;
            ApplyPunch(dt);
            ResolveShoves(dt);

            // Always, NOT only when the agent has a destination. Holding a tile is the state most of
            // the crowd spends the round in, and gating this on having a destination meant those
            // agents never separated from the player at all - which is why bodies visibly overlapped
            // Bob. Only the path RESUME needs a destination; the separation does not.
            HandlePlayerContact(dt);
            FightHeadOn(dt);
        }

        /// <summary>
        /// Take a punch. Delivered as a decaying displacement rather than an impulse force, because
        /// this body is a kinematic agent: the only thing that can move it is Agent.Move.
        ///
        /// It deliberately does NOT respect the shove force comparison. A punch is not a shove - it
        /// beats anyone, which is what makes it a verb the player chooses rather than a consequence
        /// of relative mass.
        /// </summary>
        public void Punch(Vector3 direction, float speed)
        {
            _punchVelocity = direction.normalized * speed;
        }

        void ApplyPunch(float dt)
        {
            if (_punchVelocity.sqrMagnitude < 0.0001f) return;

            if (Agent.enabled && Agent.isOnNavMesh) Agent.Move(_punchVelocity * dt);

            _punchVelocity = Vector3.MoveTowards(_punchVelocity, Vector3.zero, PunchDecay * dt);
        }

        /// <summary>
        /// NPC-vs-NPC shoving. Only the WEAKER body is displaced, by the force difference, so a
        /// contact is settled by one side rather than both pushing each other apart and
        /// double-counting the separation.
        ///
        /// The displacement covers the overlap AND keeps going while the stronger body leans in.
        /// Just un-overlapping would make shoving a no-op: bodies would be separated but never moved
        /// off the tile they are standing on, which is the whole point of pushing.
        /// </summary>
        void ResolveShoves(float dt)
        {
            float reach = BodyRadius * 2.2f + 0.4f;
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, reach,
                                                  _others, NpcMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var rb = _others[i].attachedRigidbody;
                if (rb == null || rb.transform == transform) continue;

                var other = rb.GetComponent<NpcLocomotion>();
                if (other == null) continue;

                float excess = other.PushForce - PushForce;
                if (excess <= 0f) continue;      // I am not the weaker one: nothing happens to me

                Vector3 away = transform.position - rb.transform.position;
                away.y = 0f;
                float dist = away.magnitude;
                float contact = BodyRadius + other.BodyRadius;
                if (dist >= contact) continue;

                Vector3 dir = dist > 0.001f ? away / dist : transform.forward;

                Agent.Move(dir * ((contact - dist) + excess * ShoveSpeedPerForce * dt));
                _shovedThisFrame = true;
            }
        }

        bool PlayerWithin(float range, out float distance, out Vector3 toPlayer)
        {
            distance = 0f;
            toPlayer = Vector3.zero;
            if (PlayerBody == null) return false;

            toPlayer = PlayerBody.transform.position - transform.position;
            toPlayer.y = 0f;
            distance = toPlayer.magnitude;
            return distance <= range;
        }

        void HandlePlayerContact(float dt)
        {
            if (PlayerBody == null) return;

            // Start reacting just before the capsules would actually touch.
            float contact = BodyRadius + PlayerBody.radius;
            float reach = contact + 0.12f;

            if (!PlayerWithin(reach, out float distance, out Vector3 toPlayer))
            {
                ResumePath();
                return;
            }

            Vector3 playerVelocity = PlayerBody.velocity;
            playerVelocity.y = 0f;
            float playerSpeed = playerVelocity.magnitude;

            Vector3 dirToMe = distance > 0.001f ? -toPlayer / distance : Vector3.zero;
            float closing = playerSpeed > 0.01f ? Vector3.Dot(playerVelocity / playerSpeed, dirToMe) : 0f;

            // Only give ground for a player who is genuinely bearing down on this NPC. Walking past,
            // or standing still, does not move the crowd.
            bool playerAdvancing = playerSpeed > PlayerPushMinSpeed && closing > 0.35f;

            // Player and NPC layers do not collide, so nothing in PhysX resolves an overlap between
            // them - the bodies have to be separated by hand. The separation is SPLIT by relative
            // force rather than all given to the NPC: the weaker body yields more ground, but both
            // move, so a bot does shove the player, just less than the player shoves it. Handing the
            // whole overlap to the NPC would make the player feel weightless and untouchable.
            if (distance < contact)
            {
                Vector3 escape = distance > 0.001f ? -toPlayer / distance : Vector3.forward;
                float myShare = PlayerForce / Mathf.Max(0.01f, PlayerForce + PushForce);
                Agent.Move(escape * (contact - distance) * myShare);
            }

            if (playerAdvancing)
            {
                Vector3 travel = playerVelocity / playerSpeed;
                Vector3 side = Vector3.Cross(Vector3.up, travel);
                if (Vector3.Dot(side, toPlayer) < 0f) side = -side;

                // Mostly sideways with a shove along your heading: the crowd parts around you
                // rather than being driven along in a wedge.
                Vector3 yieldDir = (side * 0.85f + travel * 0.6f).normalized;
                float scale = Mathf.Clamp01(playerSpeed / Mathf.Max(0.01f, PlayerPushSpeed));

                Agent.Move(yieldDir * (PlayerPushSpeed * scale * dt));
                ResumePath();
                return;
            }

            // Not advancing: hold the line and lean on the player.
            if (PressAgainstPlayer && !_halted)
            {
                _halted = true;
                Agent.isStopped = true;
            }
        }

        void ResumePath()
        {
            if (!_halted) return;

            _halted = false;

            // Holding on purpose: there is no path to restart, and resuming one would send the agent
            // wandering off the tile it is defending.
            if (!_hasDestination) return;
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            Agent.isStopped = false;
            Agent.SetDestination(_destination);
        }

        /// <summary>
        /// Head-on resolution. Only fires for other NPCs (they carry a Rigidbody); the player is
        /// deliberately excluded so blocking you stays their job.
        /// </summary>
        void FightHeadOn(float dt)
        {
            // A shove already resolved this contact; adding lateral avoidance on top would fight it
            // and produce jitter. The sidestep exists for the case shoving cannot settle - equal
            // forces, where neither body is the weaker one and both would otherwise stand still.
            if (!SidestepEnabled || _shovedThisFrame)
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
            Vector3 sideDir = Vector3.Cross(Vector3.up, heading);
            if (sideDir.sqrMagnitude < 0.0001f) return;
            sideDir.Normalize();

            Agent.Move(sideDir * (SidestepSpeed * ramp * dt));
        }
    }
}
