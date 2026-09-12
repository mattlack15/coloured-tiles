using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam
{
    /// <summary>
    /// WASD player. CharacterController rather than a Rigidbody on purpose: it can never be
    /// shoved by an NPC (so "you cannot push through" holds) and NPC capsule colliders stop it
    /// dead. The NPCs, meanwhile, do not avoid it at all - they only stop when they physically
    /// bump into it, so they genuinely stand in the way.
    ///
    /// Because nothing can push the player, the edge is a *voluntary* hazard: the only way to die
    /// is to take a line around a blocked lane that happens to hug the lip. That is the trade the
    /// crowd forces on you, and it is entirely your own doing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public IArena Board;
        public GameBootstrap Game;
        public Faller Faller;
        public float Speed = 4.5f;

        [Tooltip("The platform is flat, so the player has no business being above this height. Guards against crowd contact lifting the capsule: CharacterController penetration recovery picks the shortest exit vector, which is sometimes straight up.")]
        public float MaxGroundedHeight = 0.4f;

        [Tooltip("Speed penalty per body being shoved. Pushing through the crowd must not be free, or the crowd stops being a cost at all.")]
        public float PushCostPerBody = 0.25f;

        [Tooltip("Debug only: forces a movement direction and ignores the keyboard. Zero means normal input. Lets the player be driven from the editor or a test harness.")]
        public Vector2 debugDrive;

        public ColorId AssignedColor { get; private set; }
        public int Claims { get; private set; }
        public bool ClaimedThisBeat { get; private set; }

        CharacterController _cc;
        MeshRenderer _bodyRenderer;
        LayerMask _npcMask;
        Vector2Int _lastClaimCell = new Vector2Int(-999, -999);
        float _claimCooldown;
        uint _rng = 0xC0FFEEu;
        Vector3 _lastMoveDir = Vector3.forward;
        readonly Collider[] _overlap = new Collider[32];

        public void Init(IArena board, GameBootstrap game, MeshRenderer body, LayerMask npcMask,
                         ColorId startColor, Faller faller)
        {
            Board = board;
            Game = game;
            _cc = GetComponent<CharacterController>();
            _bodyRenderer = body;
            _npcMask = npcMask;
            AssignedColor = startColor;
            Faller = faller;

            Faller.Respawned += OnRespawned;
            Board.CycleAdvanced += OnBeat;

            ApplyBodyColor();
            _lastClaimCell = board.WorldToCell(transform.position);
        }

        /// <summary>The board re-rolls the player's colour once per beat, exactly like the NPCs.</summary>
        void OnBeat(int beat)
        {
            AssignedColor = Palette.NewColorDifferent(AssignedColor, ref _rng);
            ClaimedThisBeat = false;
            ApplyBodyColor();
        }

        void ApplyBodyColor()
        {
            if (_bodyRenderer != null)
                _bodyRenderer.sharedMaterial = Palette.Material(AssignedColor);
        }

        void Update()
        {
            if (Game == null || Board == null || !Board.Ready) return;

            if (Faller != null && Faller.IsAirborne) return;

            if (_claimCooldown > 0f) _claimCooldown -= Time.deltaTime;

            Vector2 input = debugDrive;
            if (input.sqrMagnitude < 0.0001f)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) input.y += 1f;
                    if (kb.sKey.isPressed) input.y -= 1f;
                    if (kb.aKey.isPressed) input.x -= 1f;
                    if (kb.dKey.isPressed) input.x += 1f;
                }
            }
            if (input.sqrMagnitude > 1f) input.Normalize();

            if (input.sqrMagnitude > 0.01f) _lastMoveDir = new Vector3(input.x, 0f, input.y).normalized;

            if (Game.RoundActive)
            {
                float speed = Speed * DensityFactor() * PushFactor();
                _cc.SimpleMove(new Vector3(input.x, 0f, input.y) * speed);
            }

            // Never let the crowd carry us upward. Several kinematic NPC bodies overlapping the
            // capsule at once can resolve by lifting it, which reads as the player floating.
            if ((Faller == null || !Faller.IsAirborne) && transform.position.y > MaxGroundedHeight)
                transform.position = new Vector3(transform.position.x, MaxGroundedHeight, transform.position.z);

            // Walked past the lip: nothing under us any more.
            if (transform.position.y < -1.2f)
            {
                Faller.BeginFall(_lastMoveDir * Speed * 0.5f + new Vector3(0f, -2f, 0f));
                return;
            }

            if (!Game.RoundActive) return;

            var cell = Board.WorldToCell(transform.position);
            if (_claimCooldown <= 0f && Board.IsLit(cell) && Board.ColourOf(cell) == AssignedColor && cell != _lastClaimCell)
                Claim(cell);
        }

        /// <summary>
        /// Optional design patch for the "tiles are never consumed" hole: a cluster of bodies is
        /// a real cost even though nobody can steal your tile. Off by default.
        /// </summary>
        float DensityFactor()
        {
            if (!Game.densitySlowdown) return 1f;

            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, 1.4f,
                                                  _overlap, _npcMask, QueryTriggerInteraction.Ignore);
            float excess = Mathf.Max(0f, n - Game.densitySlowdownAt);
            return 1f / (1f + excess * Game.densitySlowdownPerBody);
        }

        /// <summary>Cost of shoving bodies out of the way. Without this, "you can push through the
        /// crowd" would mean the crowd costs you nothing, which removes the entire premise.</summary>
        float PushFactor()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, 0.95f,
                                                  _overlap, _npcMask, QueryTriggerInteraction.Ignore);
            return 1f / (1f + Mathf.Max(0, n - 1) * PushCostPerBody);
        }

        void Claim(Vector2Int cell)
        {
            Claims++;
            ClaimedThisBeat = true;
            _claimCooldown = Game.claimCooldown;
            _lastClaimCell = cell;
            Game.OnPlayerClaim();
        }

        void OnRespawned()
        {
            var cell = new Vector2Int(Board.Width / 2, Board.Height / 2);
            transform.SetPositionAndRotation(Board.CellToWorld(cell), Quaternion.identity);

            if (_cc != null) _cc.enabled = true;
            if (Game != null) Game.SetAvoidPlayer(Game.avoidPlayer);

            _lastClaimCell = cell;
            _claimCooldown = 0f;
            Game.OnPlayerFall();
        }

        void OnDestroy()
        {
            if (Faller != null) Faller.Respawned -= OnRespawned;
            if (Board != null) Board.CycleAdvanced -= OnBeat;
        }
    }
}
