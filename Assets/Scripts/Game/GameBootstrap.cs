using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Jam
{
    /// <summary>
    /// Builds the whole prototype at runtime: platform, navmesh, player, crowd, camera.
    /// Everything is tunable from the Inspector while playing.
    ///
    /// Two deliberate choices worth knowing about:
    ///
    /// 1. NPCs do not avoid the player. The player is not a NavMeshAgent, so the RVO solver is
    ///    blind to it and the crowd simply walks into you and piles up. Press G to carve the
    ///    player into the navmesh instead and watch how much pressure that removes.
    ///
    /// 2. The platform edge is a *shared* hazard. NavMeshAgent clamps to the navmesh, so NPCs can
    ///    never walk off - they have to be squeezed off by their neighbours. See NpcBrain's
    ///    edge-slip check and the slip* knobs below.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap I { get; private set; }

        public const int PlayerLayer = 8;
        public const int NpcLayer = 9;

        [Header("Platform")]
        public int boardWidth = 14;
        public int boardHeight = 14;
        public float tileSize = 1f;
        public int seed = 20260912;

        [Header("Colour beat")]
        [Tooltip("Seconds between colour flips. Each beat also re-rolls everyone's colour, so this is both the scramble and the time limit for one claim.")]
        public float beatSeconds = 10f;
        [Tooltip("Separate blobs of each colour that light up per beat.")]
        public int litGroupsPerColor = 3;
        [Tooltip("Tiles per blob. Everything else on the board stays dark.")]
        public int litTilesPerGroup = 3;

        [Header("Crowd")]
        public int npcCount = 80;
        public float bodyRadius = 0.32f;
        public float bodyHeight = 1.4f;
        public float playerSpeed = 4.5f;
        public float npcSpeedMin = 3.6f;
        public float npcSpeedMax = 4.7f;
        [Range(0f, 1f)] public float boldShare = 0.18f;
        [Range(0f, 1f)] public float anchorShare = 0.28f;
        [Range(0f, 1f)] public float drifterShare = 0.22f;
        [Tooltip("Off: NPCs walk into the player and press. On: the player carves the navmesh when stationary, so NPCs route around it.")]
        public bool avoidPlayer = false;
        [Tooltip("The player's force advantage over the crowd. How fast an NPC slides aside when you walk into it. Higher = you bulldoze through people.")]
        public float playerPushSpeed = 2.2f;
        [Tooltip("Off (recommended): NPC/NPC collision is handled by RVO only. On: hard physics bodies, more jitter and deadlocks.")]
        public bool npcPhysicsCollisions = false;

        [Header("The void")]
        [Tooltip("How close to the lip an NPC has to be for the crowd to be able to shove it off.")]
        public float slipEdgeMargin = 1.05f;
        public float slipRadius = 1f;
        [Tooltip("Neighbours needed before a squeeze can push someone over the side.")]
        public int slipCrowd = 2;
        [Tooltip("How much the squeeze must point at the void. 0 = any direction, 1 = dead outward.")]
        [Range(-1f, 1f)] public float slipOutwardDot = 0.2f;
        [Tooltip("Seconds of sustained squeeze before the body goes over.")]
        public float slipTime = 0.4f;
        public float slipImpulse = 2.4f;
        public float slipHop = 1.6f;

        [Header("Fall cost")]
        [Tooltip("Seconds lost when the player goes over the side.")]
        public float playerFallPenalty = 3f;
        [Tooltip("How far in from the lip NPCs respawn, in tiles.")]
        public int npcRespawnMargin = 3;

        [Header("Round")]
        public float roundSeconds = 120f;
        public int targetClaims = 6;
        [Tooltip("Minimum seconds between two claims by the same agent. Relative to the beat: at a 10s beat, one claim per beat would otherwise leave ~8s of dead time after every successful claim.")]
        public float claimCooldown = 1.5f;

        [Header("Feel experiment")]
        [Tooltip("Player slows down when bodies crowd around them. The cheap fix for 'tiles are never consumed'.")]
        public bool densitySlowdown = false;
        public float densitySlowdownAt = 2f;
        public float densitySlowdownPerBody = 0.18f;

        [Header("Debug")]
        public bool drawTargetLines = false;

        public bool RoundActive { get; private set; }
        public float TimeLeft { get; private set; }
        public int NpcBest { get; private set; }
        public int NpcFalls { get; private set; }
        public int PlayerFalls { get; private set; }
        public bool Won { get; private set; }
        public BoardManager Board { get; private set; }
        public PlayerController Player { get; private set; }

        Transform _agentsRoot;
        NavMeshObstacle _playerObstacle;
        Faller _playerFaller;
        CharacterController _playerBody;
        int _spawnedNpcs;

        void Awake()
        {
            I = this;
            Application.runInBackground = true;
        }

        void Start()
        {
            CleanupStaleBuild();

            Physics.IgnoreLayerCollision(NpcLayer, NpcLayer, !npcPhysicsCollisions);
            // The player does not participate in ANY agent-agent physics. That is what makes them
            // immovable by the crowd: penetration recovery can only ever push someone, and if the
            // player is not a collision participant there is nobody to push them. NPCs resolve
            // their own overlap with the player in NpcLocomotion instead, which gives the player
            // the force advantage for free.
            Physics.IgnoreLayerCollision(PlayerLayer, NpcLayer, true);

            BuildBoard();
            BuildNavMesh();
            BuildLighting();
            BuildPlayer();
            BuildCrowd();
            BuildCamera();

            SetAvoidPlayer(avoidPlayer);
            TimeLeft = roundSeconds;
            RoundActive = true;

            // One claim per beat is the floor of what the target demands, so anything above the
            // beat count is literally unwinnable. Cheap guard against a silent design mistake.
            int beats = Mathf.Max(1, Mathf.FloorToInt(roundSeconds / Mathf.Max(0.01f, beatSeconds)));
            if (targetClaims >= beats)
                Debug.LogWarning($"[Jam] Unwinnable round: targetClaims={targetClaims} but only {beats} beats " +
                                 $"fit in roundSeconds={roundSeconds} at beatSeconds={beatSeconds}.");
        }

        /// <summary>
        /// A script recompile while in play mode re-runs Start() on a scene that still holds the
        /// objects we generated last time - and the old BoardManager's private arrays are not
        /// serialized, so it comes back hollow while the agents still point at it. Rebuild from
        /// scratch instead of layering a second platform on top of a dead one.
        /// </summary>
        void CleanupStaleBuild()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.parent != null) continue;
                if (t.name == "Board" || t.name == "Agents") DestroyImmediate(t.gameObject);
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) drawTargetLines = !drawTargetLines;
                if (kb.gKey.wasPressedThisFrame) SetAvoidPlayer(!avoidPlayer);
                if (kb.rKey.wasPressedThisFrame) Restart();
            }

            if (!RoundActive) return;

            TimeLeft -= Time.deltaTime;
            if (Player != null && Player.Claims >= targetClaims) EndRound(true);
            else if (TimeLeft <= 0f) EndRound(false);
        }

        // ------------------------------------------------------------------ construction

        void BuildBoard()
        {
            var go = new GameObject("Board");
            Board = go.AddComponent<BoardManager>();
            Board.Configure(boardWidth, boardHeight, tileSize, seed);
            Board.BeatSeconds = beatSeconds;
            Board.LitGroupsPerColor = litGroupsPerColor;
            Board.LitTilesPerGroup = litTilesPerGroup;
            Board.Generate();
        }

        void BuildNavMesh()
        {
            var surface = Board.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;

            Physics.SyncTransforms();
            surface.BuildNavMesh();
        }

        void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
            RenderSettings.fog = false;

            var light = FindDirectionalLight();
            if (light == null)
            {
                var go = new GameObject("Directional Light");
                light = go.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
        }

        static Light FindDirectionalLight()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) return l;
            return null;
        }

        void BuildPlayer()
        {
            var root = new GameObject("Player");
            root.layer = PlayerLayer;
            root.tag = "Player";

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.4f;
            cc.height = 1.5f;
            cc.center = new Vector3(0f, 0.75f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            _playerBody = cc;

            // Off by default: only the fall uses physics, so the CharacterController never has to
            // share a transform with a live Rigidbody.
            var fallCollider = root.AddComponent<CapsuleCollider>();
            fallCollider.radius = 0.4f;
            fallCollider.height = 1.5f;
            fallCollider.center = new Vector3(0f, 0.75f, 0f);
            fallCollider.enabled = false;

            var body = CreateBody(root.transform, PlayerLayer, 0.8f, 0.75f, 0.75f, false);
            CreateHalo(root.transform);

            Player = root.AddComponent<PlayerController>();

            var startCell = new Vector2Int(Board.Width / 2, Board.Height / 2);
            root.transform.position = Board.CellToWorld(startCell);

            // Optional: carving the player into the navmesh is the one way to make NPCs route
            // around you, which is precisely the pressure this prototype is exploring.
            _playerObstacle = root.AddComponent<NavMeshObstacle>();
            _playerObstacle.shape = NavMeshObstacleShape.Capsule;
            _playerObstacle.radius = 0.4f;
            _playerObstacle.height = 1.5f;
            _playerObstacle.center = new Vector3(0f, 0.75f, 0f);
            _playerObstacle.carving = true;
            _playerObstacle.carveOnlyStationary = true;
            _playerObstacle.carvingMoveThreshold = 0.15f;
            _playerObstacle.carvingTimeToStationary = 0.2f;
            _playerObstacle.enabled = false;

            _playerFaller = root.AddComponent<Faller>();
            _playerFaller.Configure(null, null, cc, _playerObstacle, fallCollider);

            Player.Init(Board, this, body, 1 << NpcLayer, NextColor(0x51ED2u), _playerFaller);

            SetAvoidPlayer(avoidPlayer);
        }

        void BuildCrowd()
        {
            _agentsRoot = new GameObject("Agents").transform;

            uint rng = (uint)seed * 2654435761u + 7u;

            for (int i = 0; i < npcCount; i++)
            {
                float speed = Mathf.Lerp(npcSpeedMin, npcSpeedMax, Palette.Hash01(ref rng));
                var archetype = PickArchetype(ref rng);
                var traits = NpcTraits.Make(archetype, speed / playerSpeed, ref rng);

                var root = new GameObject($"NPC_{i:D2}_{archetype}");
                root.layer = NpcLayer;
                root.transform.SetParent(_agentsRoot, false);

                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = bodyRadius;
                agent.height = bodyHeight;
                agent.baseOffset = 0f;
                agent.speed = speed;
                agent.acceleration = 16f;
                agent.angularSpeed = 720f;
                agent.stoppingDistance = 0.15f;
                agent.autoBraking = true;
                agent.updateRotation = true;
                agent.obstacleAvoidanceType = npcPhysicsCollisions
                    ? ObstacleAvoidanceType.NoObstacleAvoidance
                    : ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
                agent.avoidancePriority = (i * 37) % 100;

                var loco = root.AddComponent<NpcLocomotion>();
                loco.BodyRadius = bodyRadius;
                loco.PlayerMask = 1 << PlayerLayer;
                loco.NpcMask = 1 << NpcLayer;
                loco.PressAgainstPlayer = true;
                loco.PlayerPushSpeed = playerPushSpeed;
                loco.PlayerBody = _playerBody;

                var body = CreateBody(root.transform, NpcLayer, bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f, true);
                var line = CreateTargetLine(root.transform);

                var faller = root.AddComponent<Faller>();
                faller.Configure(rb, agent, null, null, null);

                var brain = root.AddComponent<NpcBrain>();

                Vector3 pos = FindSpawn(Board.CellToWorld(Board.RandomInteriorCell(2)), ref rng);
                root.transform.position = pos;

                brain.Init(Board, this, i, NextColor(rng), traits, body, line, faller);

                if (agent.isOnNavMesh) agent.Warp(pos);
                _spawnedNpcs++;
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

        Vector3 FindSpawn(Vector3 desired, ref uint rng)
        {
            // Keep a little breathing room around the player's start so the opening is readable.
            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (Player == null) break;
                if (Vector3.Distance(desired, Player.transform.position) > 4.5f) break;
                desired = Board.CellToWorld(Board.RandomInteriorCell(2));
            }

            if (NavMesh.SamplePosition(desired, out var hit, 3f, NavMesh.AllAreas)) return hit.position;
            return desired;
        }

        ColorId NextColor(uint rng)
        {
            uint state = rng;
            return (ColorId)Mathf.Clamp((int)(Palette.Hash01(ref state) * Palette.Count), 0, Palette.Count - 1);
        }

        MeshRenderer CreateBody(Transform parent, int layer, float sx, float sy, float sz, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Body";
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, sy, 0f);
            go.transform.localScale = new Vector3(sx, sy, sz);

            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
            }

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.On;
            return mr;
        }

        /// <summary>
        /// A neutral white ring over the player's head. This is how you find yourself in a crowd -
        /// not a fifth colour and not a different shade of the player's own colour.
        /// </summary>
        void CreateHalo(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Halo";
            go.layer = PlayerLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            go.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);

            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            go.GetComponent<MeshRenderer>().sharedMaterial = Palette.White;
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

        void BuildCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            cam.orthographic = true;
            cam.orthographicSize = Board.Height * tileSize * 0.58f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);

            var rig = cam.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Target = Player != null ? Player.transform : null;
            rig.Snap();
        }

        // ------------------------------------------------------------------ runtime control

        public void SetAvoidPlayer(bool value)
        {
            avoidPlayer = value;
            if (_playerObstacle != null)
                _playerObstacle.enabled = value && (_playerFaller == null || !_playerFaller.IsFalling);

            if (_agentsRoot == null) return;
            foreach (var loco in _agentsRoot.GetComponentsInChildren<NpcLocomotion>())
                loco.PressAgainstPlayer = !value;
        }

        public void OnPlayerClaim()
        {
            // Hook for future juice: sfx, pop, screen shake.
        }

        public void OnPlayerFall()
        {
            PlayerFalls++;
            TimeLeft -= playerFallPenalty;
        }

        public void OnNpcClaim(NpcBrain brain)
        {
            if (brain.Claims > NpcBest) NpcBest = brain.Claims;
        }

        public void OnNpcFall(NpcBrain brain)
        {
            NpcFalls++;
        }

        void EndRound(bool won)
        {
            RoundActive = false;
            Won = won;
        }

        void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public int SpawnedNpcs => _spawnedNpcs;
    }
}
