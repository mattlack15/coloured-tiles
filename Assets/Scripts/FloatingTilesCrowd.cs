using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Jam
{
    /// <summary>
    /// Spawns and drives the NPC crowd inside the hand-built FloatingTiles arena - the counterpart
    /// to GameBootstrap for the team's map.
    ///
    /// It exists because the two arenas differ in exactly two ways the crowd must not care about:
    /// this one needs a navmesh baked at runtime (the gameplay area is authored geometry, not
    /// generated), and its rules are round-based rather than beat-based. Both are absorbed here, so
    /// the agents are the same NpcBrain/NpcLocomotion/Faller used in SampleScene and see only
    /// IArena and ICrowdContext.
    /// </summary>
    [RequireComponent(typeof(FloatingMap))]
    public class FloatingTilesCrowd : MonoBehaviour, ICrowdContext
    {
        [Header("Crowd")]
        public int npcCount = 20;
        public float bodyRadius = 0.32f;
        public float bodyHeight = 1.4f;
        [Tooltip("Deliberately close to the player's 6: the arena is only ~11 units across, so a big speed gap makes the crowd irrelevant.")]
        public float speedMin = 4.6f;
        public float speedMax = 5.6f;
        [Range(0f, 1f)] public float boldShare = 0.18f;
        [Range(0f, 1f)] public float anchorShare = 0.28f;
        [Range(0f, 1f)] public float drifterShare = 0.22f;

        [Header("Rules")]
        public int npcRespawnMargin = 1;
        public float claimCooldown = 1.5f;
        public bool drawTargetLines = false;

        [Header("Being squeezed off the edge")]
        public float slipEdgeMargin = 1.05f;
        public float slipRadius = 1f;
        public int slipCrowd = 2;
        public float slipOutwardDot = 0.2f;
        public float slipTime = 0.4f;
        public float slipImpulse = 2.4f;
        public float slipHop = 1.6f;

        // ---- ICrowdContext ----

        /// <summary>Agents only move while the map is taking a round's worth of movement. Standing
        /// down during the drop and the judgement keeps them off a floor that is partly gone.</summary>
        public bool RoundActive => _map != null && _map.MovementAllowed;

        public int NpcRespawnMargin => npcRespawnMargin;
        public float ClaimCooldown => claimCooldown;
        public bool DrawTargetLines => drawTargetLines;
        public float SlipEdgeMargin => slipEdgeMargin;
        public float SlipRadius => slipRadius;
        public int SlipCrowd => slipCrowd;
        public float SlipOutwardDot => slipOutwardDot;
        public float SlipTime => slipTime;
        public float SlipImpulse => slipImpulse;
        public float SlipHop => slipHop;

        /// <summary>The map owns claiming here, so a claim is just bookkeeping for the HUD.</summary>
        public void OnNpcClaim(NpcBrain brain) { }

        public void OnNpcFall(NpcBrain brain) { Falls++; }

        public int Falls { get; private set; }

        FloatingMap _map;
        CrowdRegistry _registry;
        Transform _crowdRoot;
        int _spawned;

        void Awake()
        {
            _map = GetComponent<FloatingMap>();
            _registry = new CrowdRegistry();

            // Same collision policy as SampleScene: agents never participate in physics with each
            // other or with the player. Overlap is resolved in code, which is what stops a crowd of
            // kinematic bodies from shoving the player's capsule around (and lifting it).
            Physics.IgnoreLayerCollision(GameBootstrap.PlayerLayer, GameBootstrap.NpcLayer, true);
            Physics.IgnoreLayerCollision(GameBootstrap.NpcLayer, GameBootstrap.NpcLayer, true);
        }

        void Start()
        {
            BakeNavMesh();
            SpawnCrowd();
            _map.RegisterParticipants();
        }

        /// <summary>
        /// The authored arena has no baked navmesh, so build one at load. This has to happen while
        /// every tile is still present: the tiles ARE the floor, and the bake would otherwise record
        /// a board with holes in it. The baked mesh is deliberately not rebuilt when tiles drop,
        /// because nothing paths during those phases anyway.
        /// </summary>
        void BakeNavMesh()
        {
            var surface = GetComponent<NavMeshSurface>();
            if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // Exclude the agents: a collider inside the bake punches a hole wherever it stands.
            surface.layerMask = ~((1 << GameBootstrap.PlayerLayer) | (1 << GameBootstrap.NpcLayer));

            Physics.SyncTransforms();
            surface.BuildNavMesh();
        }

        void SpawnCrowd()
        {
            _crowdRoot = new GameObject("Crowd").transform;
            _crowdRoot.SetParent(transform, false);

            uint rng = 0x51ED2u;
            var playerBody = _map.player != null ? _map.player.GetComponent<CharacterController>() : null;

            for (int i = 0; i < npcCount; i++)
            {
                float speed = Mathf.Lerp(speedMin, speedMax, Palette.Hash01(ref rng));
                var archetype = PickArchetype(ref rng);
                var traits = NpcTraits.Make(archetype, speed / Mathf.Max(0.01f, speedMax), ref rng);

                var go = new GameObject($"NPC_{i:D2}_{archetype}");
                go.layer = GameBootstrap.NpcLayer;
                go.transform.SetParent(_crowdRoot, false);

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = bodyRadius;
                agent.height = bodyHeight;
                agent.baseOffset = 0f;
                agent.speed = speed;
                agent.acceleration = 16f;
                agent.angularSpeed = 720f;
                agent.stoppingDistance = 0.15f;
                agent.autoBraking = true;
                agent.updateRotation = true;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
                agent.avoidancePriority = (i * 37) % 100;

                var loco = go.AddComponent<NpcLocomotion>();
                loco.BodyRadius = bodyRadius;
                loco.PlayerMask = 1 << GameBootstrap.PlayerLayer;
                loco.NpcMask = 1 << GameBootstrap.NpcLayer;
                loco.PressAgainstPlayer = true;
                loco.PlayerPushSpeed = 2.2f;
                loco.PlayerBody = playerBody;

                // The capsule collider is kept on purpose: the sidestep's neighbour query needs a
                // collider to find, even though nothing resolves collisions between agents.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.layer = GameBootstrap.NpcLayer;
                body.transform.SetParent(go.transform, false);
                body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
                body.transform.localScale = new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f);
                var renderer = body.GetComponent<MeshRenderer>();

                var line = CreateTargetLine(go.transform);

                var faller = go.AddComponent<Faller>();
                faller.Configure(rb, agent, null, null, null);

                var participant = go.AddComponent<TileParticipant>();

                var brain = go.AddComponent<NpcBrain>();

                // Spread along the edge platform, then snap onto the navmesh: EdgeSpawnPosition
                // hands back the spawn point's height, which is above the deck.
                Vector3 pos = _map.EdgeSpawnPosition(i, npcCount);
                if (NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas)) pos = hit.position;
                go.transform.position = pos;

                brain.Init(_map, _registry, this, i, (ColorId)(i % Palette.Count),
                           traits, renderer, line, faller);
                brain.HoldAndSurvive = true;
                brain.Participant = participant;

                if (agent.isOnNavMesh) agent.Warp(pos);
                _spawned++;
            }
        }

        NpcArchetype PickArchetype(ref uint rng)
        {
            float r = Palette.Hash01(ref rng);
            if (r < boldShare) return NpcArchetype.Bold;
            if (r < boldShare + anchorShare) return NpcArchetype.Anchor;
            if (r < boldShare + anchorShare + drifterShare) return NpcArchetype.Drifter;
            return NpcArchetype.Cautious;
        }

        LineRenderer CreateTargetLine(Transform parent)
        {
            var go = new GameObject("TargetLine");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.06f;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        public int SpawnedNpcs => _spawned;
        public CrowdRegistry Registry => _registry;
    }
}
