using UnityEngine;

namespace Jam
{
    /// <summary>
    /// One competitor in a Floating Tiles round: a body, an assigned colour, and its lives.
    ///
    /// The map owns the rules; a participant only knows how to be told it is safe, ejected, or
    /// killed. Keeping that split means FloatingMap's phase machine needs no knowledge of what is
    /// driving the body — today that is the solo TestPlayer, later it is the NavMeshAgent crowd.
    /// </summary>
    [DisallowMultipleComponent]
    public class TileParticipant : MonoBehaviour
    {
        /// <summary>Index into FloatingMap's palette, or -1 when unassigned.</summary>
        public int Colour = -1;

        /// <summary>Set by the map on first registration; 0 means "not yet given any".</summary>
        public int Lives;

        /// <summary>Out of the game for good: lives are gone.</summary>
        public bool Eliminated;

        /// <summary>Fell this round. Sits out until the next round starts.</summary>
        public bool OutThisRound;

        /// <summary>The tile this participant was standing on when the black tiles dropped, -1 if none.</summary>
        public int TileIndex = -1;

        public bool Alive => !Eliminated && !OutThisRound;

        CharacterController _body;
        Rigidbody _rigidbody;
        Renderer[] _renderers;

        /// <summary>The solo player, when this participant is the human. May be null for NPCs.</summary>
        public TestPlayer Player { get; private set; }

        public CharacterController Body
        {
            get
            {
                if (_body == null) _body = GetComponent<CharacterController>();
                return _body;
            }
        }

        void Awake()
        {
            Player = GetComponent<TestPlayer>();
            _renderers = GetComponentsInChildren<Renderer>(true);

            // Adopt the body if one is already there. Crowd agents always carry a kinematic
            // Rigidbody, and AddComponent returns null when one already exists - so Eject used to
            // throw AFTER disabling the agent, leaving it to drop limply with no ejection velocity.
            // That is what read as agents "collapsing for no reason".
            _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>Lowest point of the body, used to decide which tile is underfoot.</summary>
        public Vector3 Feet
        {
            get
            {
                if (Player != null) return Player.Feet;
                if (Body != null) return Body.bounds.center - Vector3.up * Body.bounds.extents.y;
                return transform.position;
            }
        }

        public bool IsDead => Player != null ? Player.IsDead : OutThisRound || Eliminated;

        /// <summary>
        /// Ejected for being on the wrong colour: thrown up and outward, in the direction it is
        /// least likely to be rescued by a neighbouring tile.
        /// </summary>
        public void Eject(Vector3 mapCentre)
        {
            if (Player != null)
            {
                Player.LaunchOff(mapCentre);
                return;
            }

            if (OutThisRound || Eliminated) return;

            Vector3 direction = transform.position - mapCentre;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;

            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.linearVelocity = direction.normalized * 18f + Vector3.up * 9f;
            _rigidbody.angularVelocity = new Vector3(6f, 6f, 6f);
        }

        /// <summary>Lose a life. Returns true if that was the last one.</summary>
        public bool LoseLife()
        {
            if (OutThisRound || Eliminated) return false;

            OutThisRound = true;
            Lives--;
            if (Lives <= 0) Eliminated = true;
            Hide();
            return Eliminated;
        }

        public void Hide()
        {
            if (Player != null) { Player.Die(); return; }

            // A generic body has to be actively parked, or it keeps accelerating through the world
            // for the rest of the round and comes back with absurd velocity.
            foreach (var r in _renderers) if (r != null) r.enabled = false;

            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            if (Body != null) Body.enabled = false;

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }
        }

        /// <summary>Put the body back on the edge platform at the start of a round.</summary>
        public void RespawnAt(Vector3 position)
        {
            OutThisRound = false;
            TileIndex = -1;

            if (Player != null)
            {
                Player.Respawn(position);
                foreach (var r in _renderers) if (r != null) r.enabled = true;
                return;
            }

            foreach (var r in _renderers) if (r != null) r.enabled = true;

            if (Body != null) Body.enabled = true;

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }

            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(position);
            }

            transform.SetPositionAndRotation(position, Quaternion.identity);
        }
    }
}
