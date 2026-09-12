using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Owns the tile grid and the colour beat.
    ///
    /// The arena is a bare floating platform: no walls, no cover. The only terrain feature is the
    /// edge, and the edge kills.
    ///
    /// Most of the board is DARK. Each beat lights only a handful of tiles in small blobs - a few
    /// tiles of each colour - so reaching your colour is a genuine trip across the platform rather
    /// than a step to the nearest match. That is what makes the crowd matter: everyone of a given
    /// colour is heading for the same three or four spots.
    /// </summary>
    public class BoardManager : MonoBehaviour
    {
        int _width = 14;
        int _height = 14;
        float _tileSize = 1f;
        int _seed = 1;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public float TileSize => _tileSize;

        /// <summary>False until Generate() has run. Guards against a script recompile during play
        /// mode leaving behind a BoardManager whose private arrays were never serialized.</summary>
        public bool Ready => _tiles != null;

        public float BeatSeconds { get; set; } = 2.5f;
        public int Beat { get; private set; }
        public float BeatProgress => BeatSeconds <= 0f ? 0f : Mathf.Clamp01(_beatTimer / BeatSeconds);

        /// <summary>How many separate blobs of each colour appear per beat.</summary>
        public int LitGroupsPerColor { get; set; } = 3;

        /// <summary>How many tiles each blob covers.</summary>
        public int LitTilesPerGroup { get; set; } = 3;

        public event System.Action<int> BeatAdvanced;

        Tile[,] _tiles;
        bool[,] _lit;
        int[,] _inbound;            // agents that have committed to this tile as a target
        int[,] _occupancy;          // agents physically standing on this tile
        readonly List<Vector2Int>[] _tilesOfColor = new List<Vector2Int>[Palette.Count];

        System.Random _rng;
        float _beatTimer;
        ColorId _playerColor;
        int _tilesOfPlayerColor;

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public void Configure(int width, int height, float tileSize, int seed)
        {
            _width = width;
            _height = height;
            _tileSize = tileSize;
            _seed = seed;
        }

        // ------------------------------------------------------------------ generation

        public void Generate()
        {
            Width = _width;
            Height = _height;
            _rng = new System.Random(_seed);
            _beatTimer = 0f;
            Beat = 0;

            _tiles = new Tile[Width, Height];
            _lit = new bool[Width, Height];
            _inbound = new int[Width, Height];
            _occupancy = new int[Width, Height];

            for (int c = 0; c < Palette.Count; c++) _tilesOfColor[c] = new List<Vector2Int>();

            BuildPlatform();
            BuildTiles();
            StartBeat();
        }

        void BuildPlatform()
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Platform";
            slab.transform.SetParent(transform, false);
            slab.transform.localPosition = new Vector3(0f, -0.6f, 0f);
            slab.transform.localScale = new Vector3(Width * _tileSize, 1.2f, Height * _tileSize);
            slab.GetComponent<MeshRenderer>().sharedMaterial = Palette.Surface(new Color(0.09f, 0.09f, 0.11f));
        }

        void BuildTiles()
        {
            var root = new GameObject("Tiles").transform;
            root.SetParent(transform, false);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Tile_{x}_{y}";
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = CellToLocal(new Vector2Int(x, y), 0.02f);
                    go.transform.localScale = new Vector3(_tileSize * 0.96f, 0.04f, _tileSize * 0.96f);

                    // Tiles must never block navigation; the platform slab carries the collider.
                    var col = go.GetComponent<Collider>();
                    if (col != null) DestroyImmediate(col);

                    var tile = go.AddComponent<Tile>();
                    tile.Cell = new Vector2Int(x, y);
                    tile.Bind(go.GetComponent<MeshRenderer>());
                    tile.SetDark();
                    _tiles[x, y] = tile;
                }
            }
        }

        // ------------------------------------------------------------------ beat

        void Update()
        {
            if (!Ready || BeatSeconds <= 0f) return;

            _beatTimer += Time.deltaTime;
            if (_beatTimer >= BeatSeconds)
            {
                _beatTimer -= BeatSeconds;
                Beat++;
                StartBeat();
                BeatAdvanced?.Invoke(Beat);
            }
        }

        /// <summary>Douse everything, then light a few small blobs. Nothing is predictable about
        /// the next beat, so no NPC can pre-position for it.</summary>
        void StartBeat()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _lit[x, y] = false;

            for (int c = 0; c < Palette.Count; c++) _tilesOfColor[c].Clear();

            _tilesOfPlayerColor = 0;

            for (int c = 0; c < Palette.Count; c++)
                for (int g = 0; g < LitGroupsPerColor; g++)
                    LightBlob((ColorId)c, LitTilesPerGroup);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    var t = _tiles[x, y];
                    if (_lit[x, y]) continue;
                    t.SetDark();
                }
            }

            foreach (var cell in _tilesOfColor[(int)_playerColor])
                if (_lit[cell.x, cell.y]) _tilesOfPlayerColor++;
        }

        void LightBlob(ColorId c, int count)
        {
            var cells = new List<Vector2Int>();

            // Find a free seed for the blob.
            Vector2Int seed = new Vector2Int(-1, -1);
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var cand = new Vector2Int(_rng.Next(Width), _rng.Next(Height));
                if (_lit[cand.x, cand.y]) continue;
                seed = cand;
                break;
            }
            if (seed.x < 0) return;

            _lit[seed.x, seed.y] = true;
            cells.Add(seed);
            Light(seed, c);

            // Grow the blob outward so each colour forms a readable cluster.
            for (int attempt = 0; attempt < count * 12 && cells.Count < count; attempt++)
            {
                var from = cells[_rng.Next(cells.Count)];
                var cand = new Vector2Int(from.x + _rng.Next(-1, 2), from.y + _rng.Next(-1, 2));
                if (!InBounds(cand.x, cand.y) || _lit[cand.x, cand.y]) continue;

                _lit[cand.x, cand.y] = true;
                cells.Add(cand);
                Light(cand, c);
            }
        }

        void Light(Vector2Int cell, ColorId c)
        {
            _tiles[cell.x, cell.y].SetLit(c);
            _tilesOfColor[(int)c].Add(cell);
        }

        public int TilesMatchingPlayerColor => _tilesOfPlayerColor;
        public int LitTileCount
        {
            get
            {
                int n = 0;
                for (int c = 0; c < Palette.Count; c++) n += _tilesOfColor[c].Count;
                return n;
            }
        }

        public void SetPlayerTargetColor(ColorId c)
        {
            _playerColor = c;
            if (!Ready) return;

            _tilesOfPlayerColor = _tilesOfColor[(int)c].Count;
        }

        public ColorId NewColorDifferent(ColorId avoid, ref uint rng)
        {
            int pick = (int)(Palette.Hash01(ref rng) * (Palette.Count - 1));
            int a = (int)avoid;
            if (pick >= a) pick++;
            return (ColorId)Mathf.Clamp(pick, 0, Palette.Count - 1);
        }

        // ------------------------------------------------------------------ geometry

        public Vector3 PlatformCenter => transform.position;

        /// <summary>Horizontal distance from the platform lip. Negative once you are past it.</summary>
        public float DistanceToEdge(Vector3 world)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            float hx = Width * _tileSize * 0.5f;
            float hz = Height * _tileSize * 0.5f;
            return Mathf.Min(hx - Mathf.Abs(l.x), hz - Mathf.Abs(l.z));
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>Everything is walkable - there are no walls. Kept for the pathfinder.</summary>
        public bool Walkable(int x, int y) => InBounds(x, y);
        public bool Walkable(Vector2Int c) => Walkable(c.x, c.y);

        public Tile GetTile(Vector2Int c) => Ready && InBounds(c.x, c.y) ? _tiles[c.x, c.y] : null;
        public List<Vector2Int> TilesOfColor(ColorId c) => _tilesOfColor[(int)c];

        public Vector2Int WorldToCell(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            int x = Mathf.RoundToInt(local.x / _tileSize + (Width - 1) * 0.5f);
            int y = Mathf.RoundToInt(local.z / _tileSize + (Height - 1) * 0.5f);
            return new Vector2Int(Mathf.Clamp(x, 0, Width - 1), Mathf.Clamp(y, 0, Height - 1));
        }

        public Vector3 CellToWorld(Vector2Int c) => transform.TransformPoint(CellToLocal(c, 0f));

        Vector3 CellToLocal(Vector2Int c, float y) => new Vector3(
            (c.x - (Width - 1) * 0.5f) * _tileSize,
            y,
            (c.y - (Height - 1) * 0.5f) * _tileSize);

        public Vector2Int NearestWalkable(Vector2Int c) => c;

        /// <summary>A cell at least <paramref name="margin"/> tiles in from the lip.</summary>
        public Vector2Int RandomInteriorCell(int margin)
        {
            int lo = Mathf.Clamp(margin, 0, Mathf.Min(Width, Height) / 2 - 1);
            int span = Mathf.Max(1, Width - lo * 2);
            int spanZ = Mathf.Max(1, Height - lo * 2);
            return new Vector2Int(_rng.Next(lo, lo + span), _rng.Next(lo, lo + spanZ));
        }

        // ------------------------------------------------------------------ congestion bookkeeping

        public int Inbound(Vector2Int c) => Ready && InBounds(c.x, c.y) ? _inbound[c.x, c.y] : 0;
        public void AddInbound(Vector2Int c) { if (Ready && InBounds(c.x, c.y)) _inbound[c.x, c.y]++; }
        public void RemoveInbound(Vector2Int c) { if (Ready && InBounds(c.x, c.y)) _inbound[c.x, c.y] = Mathf.Max(0, _inbound[c.x, c.y] - 1); }

        public void AddOccupant(Vector2Int c) { if (Ready && InBounds(c.x, c.y)) _occupancy[c.x, c.y]++; }
        public void RemoveOccupant(Vector2Int c) { if (Ready && InBounds(c.x, c.y)) _occupancy[c.x, c.y] = Mathf.Max(0, _occupancy[c.x, c.y] - 1); }

        public int Occupancy(Vector2Int c) => Ready && InBounds(c.x, c.y) ? _occupancy[c.x, c.y] : 0;

        public int OccupancyAround(Vector2Int c, int radius)
        {
            if (!Ready) return 0;
            int total = 0;
            for (int x = c.x - radius; x <= c.x + radius; x++)
                for (int y = c.y - radius; y <= c.y + radius; y++)
                    if (InBounds(x, y)) total += _occupancy[x, y];
            return total;
        }

        // ------------------------------------------------------------------ pathing

        /// <summary>
        /// Single-source field of step counts, 8-directional, no corner cutting. At this board size
        /// a per-agent field is far cheaper than maintaining flow fields, so each brain just re-runs
        /// this when it needs distances.
        /// </summary>
        public void BfsFrom(Vector2Int from, int[,] dst)
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    dst[x, y] = -1;

            if (!Walkable(from)) return;

            var q = new Queue<Vector2Int>();
            dst[from.x, from.y] = 0;
            q.Enqueue(from);

            while (q.Count > 0)
            {
                var c = q.Dequeue();
                int nd = dst[c.x, c.y] + 1;

                for (int i = 0; i < 8; i++)
                {
                    int nx = c.x + Dx[i], ny = c.y + Dy[i];
                    if (!Walkable(nx, ny)) continue;
                    if (dst[nx, ny] >= 0) continue;

                    if (i >= 4 && (!Walkable(c.x + Dx[i], c.y) || !Walkable(c.x, c.y + Dy[i]))) continue;

                    dst[nx, ny] = nd;
                    q.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
    }
}
