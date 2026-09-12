using UnityEngine;
using UnityEngine.AI;

namespace Jam
{
    /// <summary>
    /// Generic "fell off the platform" handler for both the player and NPCs.
    ///
    /// The character/agent is switched off, a real Rigidbody takes over, and gravity does the
    /// rest. The owner gets a Respawned callback to decide where to put the body back and what it
    /// should cost - which is the only part that differs between the player and an NPC.
    ///
    /// Two ordering traps are handled here, both of which silently corrupt the respawn pose:
    ///
    ///  * Destroy() is deferred to the end of the frame, so a Rigidbody that is merely scheduled
    ///    for destruction keeps simulating and re-applies its tumble on top of the pose the owner
    ///    just set. The body is therefore parked kinematic *before* anything else happens.
    ///
    ///  * FixedUpdate runs before the physics step, so teleporting from inside it gets overwritten
    ///    by that same step. The Respawned callback is queued and fired from Update instead.
    /// </summary>
    public class Faller : MonoBehaviour
    {
        public float KillY = -9f;

        /// <summary>Stand the body back up when it respawns. Without this the character keeps the
        /// rotation it ended the tumble with and comes back lying on its side.</summary>
        public bool ResetRotationOnRespawn = true;

        public bool IsFalling { get; private set; }

        /// <summary>True from the moment a fall starts until the respawn teleport has been
        /// delivered. Owners MUST treat this as "hands off": between the FixedUpdate that ends the
        /// fall and the Update that delivers the teleport the body is still parked far below the
        /// platform, and anything that reacts to that position would start a second, phantom
        /// fall.</summary>
        public bool IsAirborne => IsFalling || _respawnPending;

        /// <summary>Raised the moment a fall starts (good place for a scream sfx).</summary>
        public event System.Action Fell;

        /// <summary>Raised from Update, once the body has been parked upright below the platform.</summary>
        public event System.Action Respawned;

        Rigidbody _rb;
        bool _ownsRigidbody;
        bool _respawnPending;
        NavMeshAgent _agent;
        CharacterController _cc;
        NavMeshObstacle _obstacle;
        Collider _fallCollider;

        /// <summary>
        /// <paramref name="persistentRigidbody"/> is the NPC's existing kinematic body, which also
        /// carries the capsule that blocks the player and so must survive. Pass null for the
        /// player: a Rigidbody next to a CharacterController is best avoided, so it is created for
        /// the fall and destroyed afterwards.
        /// </summary>
        public void Configure(Rigidbody persistentRigidbody, NavMeshAgent agent, CharacterController cc,
                              NavMeshObstacle obstacle, Collider fallCollider)
        {
            _agent = agent;
            _cc = cc;
            _obstacle = obstacle;
            _fallCollider = fallCollider;

            if (persistentRigidbody != null)
            {
                _rb = persistentRigidbody;
                _ownsRigidbody = false;
                ParkRigidbody();
            }
            else
            {
                _ownsRigidbody = true;
            }
        }

        /// <summary>Make the body inert: no gravity, no inherited velocity, no interpolation
        /// fighting the transform. Safe to call on an already-kinematic body.</summary>
        void ParkRigidbody()
        {
            if (_rb == null) return;

            if (!_rb.isKinematic)
            {
                // Velocities can only be cleared while the body is still dynamic.
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.None;
        }

        public void BeginFall(Vector3 velocity, float spin = 7f)
        {
            if (IsAirborne) return;

            IsFalling = true;
            Fell?.Invoke();

            if (_agent != null) { _agent.isStopped = true; _agent.enabled = false; }
            if (_cc != null) _cc.enabled = false;
            if (_obstacle != null) _obstacle.enabled = false;
            if (_fallCollider != null) _fallCollider.enabled = true;

            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = new Vector3(Random.Range(-spin, spin), Random.Range(-spin, spin), Random.Range(-spin, spin));
        }

        void FixedUpdate()
        {
            if (!IsFalling) return;
            if (transform.position.y > KillY) return;

            IsFalling = false;
            if (_fallCollider != null) _fallCollider.enabled = false;

            ParkRigidbody();
            if (ResetRotationOnRespawn) transform.rotation = Quaternion.identity;

            if (_ownsRigidbody)
            {
                Destroy(_rb);
                _rb = null;
            }

            // Cleared before the callback so the owner is free to move normally from here on.
            _respawnPending = true;
        }

        void Update()
        {
            if (!_respawnPending) return;
            _respawnPending = false;
            Respawned?.Invoke();
        }
    }
}
