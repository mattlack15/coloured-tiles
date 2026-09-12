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
        public BoardManager Board;
        public GameBootstrap Game;
        public Faller Faller;
        public float Speed = 4.5f;

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

        public void Init(BoardManager board, GameBootstrap game, MeshRenderer body, LayerMask npcMask,
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
            Board.BeatAdvanced += OnBeat;

            ApplyBodyColor();
            Board.SetPlayerTargetColor(AssignedColor);
            _lastClaimCell = board.WorldToCell(transform.position);
        }

        /// <summary>The board re-rolls the player's colour once per beat, exactly like the NPCs.</summary>
        void OnBeat(int beat)
        {
            AssignedColor = Board.NewColorDifferent(AssignedColor, ref _rng);
            ClaimedThisBeat = false;
            ApplyBodyColor();
            Board.SetPlayerTargetColor(AssignedColor);
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

            Vector2 input = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
            }
            if (input.sqrMagnitude > 1f) input.Normalize();

            if (input.sqrMagnitude > 0.01f) _lastMoveDir = new Vector3(input.x, 0f, input.y).normalized;

            if (Game.RoundActive)
            {
                float speed = Speed * DensityFactor();
                _cc.SimpleMove(new Vector3(input.x, 0f, input.y) * speed);
            }

            // Walked past the lip: nothing under us any more.
            if (transform.position.y < -1.2f)
            {
                Faller.BeginFall(_lastMoveDir * Speed * 0.5f + new Vector3(0f, -2f, 0f));
                return;
            }

            if (!Game.RoundActive) return;

            var cell = Board.WorldToCell(transform.position);
            var tile = Board.GetTile(cell);
            if (_claimCooldown <= 0f && tile != null && tile.Lit && tile.Current == AssignedColor && cell != _lastClaimCell)
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
            if (Board != null) Board.BeatAdvanced -= OnBeat;
        }
    }
}
