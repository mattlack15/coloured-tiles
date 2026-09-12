using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Beat-driven colour chaser.
    ///
    /// The rhythm is owned by the board: once per beat a few tiles light up in each colour, and
    /// every agent is given a new colour. An agent's colour is therefore fixed for the whole beat -
    /// it never changes mid-journey - and the agent has exactly one beat to reach a lit tile of
    /// that colour and claim it.
    ///
    /// Within a beat the agent picks one lit tile and commits to it. Commitment is the point: it
    /// kills target dithering, it makes the crowd actually travel instead of milling around the
    /// nearest match, and it makes NPCs predictable enough that the player can cut them off.
    ///
    /// This file also owns the crowd's relationship with the void. NavMeshAgent clamps itself to
    /// the navmesh, so an NPC can never walk off the platform on its own - it has to be *shoved*.
    /// That is what CheckEdgeSlip does, and it is the entire reason the crowd thins itself out.
    /// </summary>
    public class NpcBrain : MonoBehaviour
    {
        public BoardManager Board;
        public NpcLocomotion Loco;
        public GameBootstrap Game;
        public NpcTraits Traits;
        public ColorId AssignedColor;
        public int Id;
        [HideInInspector] public Faller Faller;

        public int Claims { get; private set; }
        public Vector2Int TargetCell { get; private set; } = new Vector2Int(-1, -1);
        public bool ClaimedThisBeat { get; private set; }

        const float ArriveRadius = 0.55f;

        int[,] _scratch;
        MeshRenderer _bodyRenderer;
        LineRenderer _line;
        readonly Collider[] _neighbours = new Collider[32];

        float _reactTimer;
        float _reconsiderTimer;
        float _stuckTimer;
        float _slipTimer;
        float _claimCooldown;
        uint _rng;
        Vector3 _lastPos;
        Vector3 _approachOffset;
        Vector2Int _occupancyCell = new Vector2Int(-1, -1);
        Vector2Int _lastClaimCell = new Vector2Int(-999, -999);

        public void Init(BoardManager board, GameBootstrap game, int id, ColorId color,
                         NpcTraits traits, MeshRenderer body, LineRenderer line, Faller faller)
        {
            Board = board;
            Game = game;
            Loco = GetComponent<NpcLocomotion>();
            Faller = faller;
            Id = id;
            AssignedColor = color;
            Traits = traits;
            _bodyRenderer = body;
            _line = line;

            _scratch = new int[board.Width, board.Height];
            _rng = (uint)(0x1234567u + id * 2654435761u);

            // Give each agent its own aim point inside the target tile, so two agents running for
            // the same tile do not walk the identical line into each other.
            _approachOffset = new Vector3(
                (Palette.Hash01(ref _rng) - 0.5f) * 0.44f,
                0f,
                (Palette.Hash01(ref _rng) - 0.5f) * 0.44f);
            _lastPos = transform.position;
            _reactTimer = Palette.Hash01(ref _rng) * traits.ReactionDelay;
            _reconsiderTimer = traits.ReconsiderInterval;
            // Blocks a free claim on the spawn tile: they have to actually move for their first point.
            _lastClaimCell = board.WorldToCell(transform.position);

            Faller.Respawned += OnRespawned;
            Board.BeatAdvanced += OnBeat;

            ApplyBodyColor();
        }

        void ApplyBodyColor()
        {
            if (_bodyRenderer != null)
                _bodyRenderer.sharedMaterial = Palette.Material(AssignedColor);
            if (_line != null)
                _line.sharedMaterial = Palette.White;
        }

        /// <summary>The board re-rolls everyone's colour once per beat. This is the only place an
        /// NPC's colour ever changes.</summary>
        void OnBeat(int beat)
        {
            AssignedColor = Board.NewColorDifferent(AssignedColor, ref _rng);
            ApplyBodyColor();

            ClaimedThisBeat = false;
            ReleaseTarget();

            // Stagger the reaction so eighty NPCs do not all pivot on the same frame. This delay is
            // a real cost: a slow agent loses the first part of its beat to thinking.
            _reactTimer = Palette.Hash01(ref _rng) * Traits.ReactionDelay;
            _reconsiderTimer = Traits.ReconsiderInterval;
            _stuckTimer = 0f;
        }

        void Update()
        {
            if (Game == null || Board == null) return;

            if (Faller != null && Faller.IsAirborne)
            {
                ReleaseOccupancy();
                ReleaseTarget();
                _line.enabled = false;
                return;
            }

            if (!Game.RoundActive || !Board.Ready)
            {
                Loco.Halt();
                UpdateOccupancy();
                return;
            }

            float dt = Time.deltaTime;
            if (_claimCooldown > 0f) _claimCooldown -= dt;
            var cell = Board.WorldToCell(transform.position);
            UpdateOccupancy(cell);
            TrackProgress(dt);

            // Squeezed against the lip? The crowd can put you over the side.
            CheckEdgeSlip(dt);

            // Standing on a lit tile of my colour - claim it, subject to the shared cooldown.
            var here = Board.GetTile(cell);
            if (_claimCooldown <= 0f && here != null && here.Lit && here.Current == AssignedColor && cell != _lastClaimCell)
            {
                Claim(cell);
                return;
            }

            bool committed = TargetCell.x >= 0 && IsTargetStillValid() && !Arrived();

            if (!committed)
            {
                // No usable target: wait out the reaction delay, then pick one.
                _reactTimer -= dt;
                if (_reactTimer <= 0f) Decide(false);
            }
            else
            {
                // En route: only rarely second-guess, and only for a clearly better tile.
                _reconsiderTimer -= dt;
                if (_reconsiderTimer <= 0f) Decide(true);
            }

            UpdateLine();
        }

        bool IsTargetStillValid()
        {
            var t = Board.GetTile(TargetCell);
            return t != null && t.Lit && t.Current == AssignedColor;
        }

        bool Arrived()
        {
            if (TargetCell.x < 0) return false;
            Vector3 goal = Board.CellToWorld(TargetCell);
            Vector3 d = goal - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= ArriveRadius * ArriveRadius;
        }

        void TrackProgress(float dt)
        {
            if (Vector3.Distance(transform.position, _lastPos) < 0.02f) _stuckTimer += dt;
            else _stuckTimer = 0f;
            _lastPos = transform.position;

            if (_stuckTimer < Traits.Patience) return;

            ReleaseTarget();
            _stuckTimer = 0f;
        }

        // ------------------------------------------------------------------ the void

        void CheckEdgeSlip(float dt)
        {
            if (Faller == null || Game.slipCrowd <= 0) return;

            if (Board.DistanceToEdge(transform.position) > Game.slipEdgeMargin)
            {
                _slipTimer = 0f;
                return;
            }

            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, Game.slipRadius,
                                                  _neighbours, 1 << GameBootstrap.NpcLayer, QueryTriggerInteraction.Ignore);

            Vector3 push = Vector3.zero;
            int counted = 0;
            for (int i = 0; i < n; i++)
            {
                var rb = _neighbours[i].attachedRigidbody;
                if (rb == null || rb.transform == transform) continue;

                Vector3 d = transform.position - rb.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) continue;

                push += d.normalized;
                counted++;
            }

            if (counted < Game.slipCrowd) { _slipTimer = 0f; return; }
            if (push.sqrMagnitude < 0.01f) { _slipTimer = 0f; return; }

            Vector3 outward = transform.position - Board.PlatformCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            outward.Normalize();

            // The squeeze has to actually point at the void, otherwise this is just jostling.
            if (Vector3.Dot(push.normalized, outward) < Game.slipOutwardDot) { _slipTimer = 0f; return; }

            _slipTimer += dt;
            if (_slipTimer < Game.slipTime) return;

            _slipTimer = 0f;
            ReleaseTarget();
            ReleaseOccupancy();

            Faller.BeginFall(outward * Game.slipImpulse + Vector3.up * Game.slipHop);
            Game.OnNpcFall(this);
        }

        void OnRespawned()
        {
            var cell = Board.RandomInteriorCell(Game.npcRespawnMargin);
            var pos = Board.CellToWorld(cell);
            transform.SetPositionAndRotation(pos, Quaternion.identity);

            if (Loco.Agent != null)
            {
                Loco.Agent.enabled = true;
                Loco.Agent.Warp(pos);
            }

            ReleaseTarget();
            _lastClaimCell = cell;
            _occupancyCell = new Vector2Int(-1, -1);
            _claimCooldown = 0f;
            _reactTimer = 0f;
            _reconsiderTimer = Traits.ReconsiderInterval;
            _stuckTimer = 0f;
            _slipTimer = 0f;
            _lastPos = pos;
            UpdateOccupancy(cell);
        }

        // ------------------------------------------------------------------ decisions

        /// <summary>
        /// Picks a lit tile of my colour. <paramref name="keepUnlessClearlyBetter"/> applies the
        /// commitment rule: an agent already walking somewhere only switches if the rival tile is
        /// better by a clear margin, which is what stops it oscillating between two options.
        /// </summary>
        void Decide(bool keepUnlessClearlyBetter)
        {
            var myCell = Board.WorldToCell(transform.position);
            Board.BfsFrom(myCell, _scratch);

            float bestScore = float.NegativeInfinity;
            Vector2Int best = new Vector2Int(-1, -1);

            var list = Board.TilesOfColor(AssignedColor);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                int d = _scratch[t.x, t.y];
                if (d < 0) continue;

                float s = Score(t, d);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = t;
                }
            }

            if (best.x < 0)
            {
                ReleaseTarget();
                return;
            }

            if (keepUnlessClearlyBetter && TargetCell.x >= 0 && best != TargetCell && IsTargetStillValid())
            {
                int currentDistance = _scratch[TargetCell.x, TargetCell.y];
                if (currentDistance >= 0)
                {
                    float currentScore = Score(TargetCell, currentDistance);
                    if (bestScore < currentScore + Traits.SwitchMargin)
                    {
                        _reconsiderTimer = Traits.ReconsiderInterval;
                        return;
                    }
                }
            }

            if (best == TargetCell) { _reconsiderTimer = Traits.ReconsiderInterval; return; }

            ReleaseTarget();
            TargetCell = best;
            Board.AddInbound(best);
            Loco.SetTarget(Board.CellToWorld(best) + _approachOffset);

            _reactTimer = 0f;
            _reconsiderTimer = Traits.ReconsiderInterval;
            _stuckTimer = 0f;
        }

        /// <summary>
        /// The single place NPC "competition" is expressed.
        ///
        /// Distance is rewarded rather than penalised, because a crowd that always takes the nearest
        /// match never goes anywhere. The congestion terms then spread the crowd across the handful
        /// of lit tiles instead of forming one lemming blob on the closest one.
        ///
        /// Note there is deliberately no edge term. NPCs do not fear the void, because a crowd that
        /// carefully avoids the lip would never push anyone off it.
        /// </summary>
        float Score(Vector2Int tile, int distance)
        {
            float travel = Traits.PrefersFar ? distance : -distance;
            float s = travel * Traits.DistanceWeight;

            float contest = Board.Inbound(tile) + Board.OccupancyAround(tile, 2) * 0.4f;
            s -= contest * Traits.CongestionWeight;

            if (Traits.PreferredDistance > 0f)
                s -= Mathf.Abs(distance - Traits.PreferredDistance) * 0.9f;

            // Stable-ish tie-break noise so identical tiles do not all win for identical agents.
            s += Palette.Hash01(ref _rng) * 0.25f;

            return s;
        }

        void ReleaseTarget()
        {
            if (Board != null && TargetCell.x >= 0) Board.RemoveInbound(TargetCell);
            TargetCell = new Vector2Int(-1, -1);
        }

        void Claim(Vector2Int cell)
        {
            Claims++;
            ClaimedThisBeat = true;
            _claimCooldown = Game.claimCooldown;
            _lastClaimCell = cell;
            ReleaseTarget();
            Game.OnNpcClaim(this);

            // Deliberately keeps moving: after claiming it immediately picks another far lit tile,
            // so the crowd stays in motion for the whole beat instead of parking.
            _reactTimer = 0f;
        }

        void UpdateOccupancy()
        {
            if (Board == null || !Board.Ready) return;
            UpdateOccupancy(Board.WorldToCell(transform.position));
        }

        void UpdateOccupancy(Vector2Int cell)
        {
            if (cell == _occupancyCell) return;
            ReleaseOccupancy();
            Board.AddOccupant(cell);
            _occupancyCell = cell;
        }

        void ReleaseOccupancy()
        {
            if (Board == null || !Board.Ready) return;
            if (_occupancyCell.x < 0) return;
            Board.RemoveOccupant(_occupancyCell);
            _occupancyCell = new Vector2Int(-1, -1);
        }

        void UpdateLine()
        {
            if (_line == null) return;

            bool on = Game.drawTargetLines && TargetCell.x >= 0;
            _line.enabled = on;
            if (!on) return;

            var from = transform.position + Vector3.up * 0.9f;
            var to = Board.CellToWorld(TargetCell) + Vector3.up * 0.05f;
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
        }

        void OnDestroy()
        {
            if (Faller != null) Faller.Respawned -= OnRespawned;
            if (Board != null) Board.BeatAdvanced -= OnBeat;
            if (Board == null || !Board.Ready) return;
            ReleaseOccupancy();
            ReleaseTarget();
        }
    }
}
