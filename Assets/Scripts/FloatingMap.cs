using System.Collections;
using System.Collections.Generic;
using Jam;
using UnityEngine;

/// <summary>
/// Round director for the floating tile map. Owns the loop; knows nothing about what is driving
/// each body.
///
/// One round:
///   1. all tiles black, everyone on the edge platform
///   2. some tiles light up and every participant is dealt a colour
///   3. moveSeconds to reach a lit tile of your colour, shoving others off
///   4. the black tiles drop away - anyone not on a lit tile falls
///   5. anyone standing on the wrong colour is launched off the edge
///   6. the black tiles return and the lit colours fade back to black
///
/// Falling costs a life; three lives and you are out. Anyone who fell sits out the rest of the
/// round and respawns on the edge platform when the next one starts. Survivors never move.
/// </summary>
public class FloatingMap : MonoBehaviour, IArena
{
    [Header("Phase timing")]
    [Min(0)] public float initialSeconds = 10;
    [Min(0)] public float moveSeconds = 15;
    [Min(0)] public float dropSeconds = 1;
    [Min(2)] public float resolveSeconds = 4;
    [Min(0.01f)] public float fadeSeconds = 1;

    [Header("Tiles")]
    [Tooltip("Lit tiles per participant. Below 1.0 there are fewer safe tiles than people, so somebody has to lose the scramble.")]
    [Range(0.2f, 2f)] public float litTilesPerParticipant = 0.85f;
    [Tooltip("Floor on the lit tile count, so a solo player still gets a real choice.")]
    [Min(4)] public int minLitTiles = 8;

    [Header("Lives")]
    [Min(1)] public int startingLives = 3;

    public Transform spawnPoint;
    public Renderer[] tiles;
    public Renderer[] platforms;
    public TestPlayer player;

    /// <summary>True once the arena has built itself, so a spawner can wait for it.</summary>
    public bool MapReady => _ready;

    /// <summary>True only while participants are allowed to run for a tile. The crowd stands down
    /// for the drop, the judgement and the reset, because there is no floor to path on and the map
    /// alone decides who survives.</summary>
    public bool MovementAllowed { get; private set; }

    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    public int RoundNumber { get; private set; }
    public bool GameOver { get; private set; }

    readonly List<TileParticipant> participants = new List<TileParticipant>();
    public IReadOnlyList<TileParticipant> Participants => participants;

    // Index order matches Jam.ColorId (Red, Green, Blue, Yellow) so the crowd can read this arena
    // through that enum. The RGB values are the original ones, just re-ordered.
    readonly Color black = new Color(.015f, .018f, .025f);
    readonly Color[] colours = { new Color(1, .08f, .12f), new Color(.05f, 1, .3f), new Color(.05f, .35f, 1), new Color(1, .85f, .02f) };
    readonly string[] names = { "RED", "GREEN", "BLUE", "YELLOW" };

    readonly List<Material> owned = new List<Material>();
    int[] tileColours;
    Transform[] outlines;

    // ---- arena metrics, derived from the tiles in Start ----
    readonly List<Vector2Int>[] _tilesByColour = new List<Vector2Int>[4];
    static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] StepY = { 0, 0, 1, -1, 1, -1, 1, -1 };
    int _gridW = 5, _gridH = 5;
    float _spacing = 2.2f;
    Vector3 _cellOrigin;
    float _standY;
    float _halfX, _halfZ;
    bool _ready;

    /// <summary>Raised when colours are re-dealt, which is the only moment an agent's colour changes.</summary>
    public event System.Action<int> CycleAdvanced;

    void Awake()
    {
        // Built here rather than in Start: another component's Start may spawn a crowd, and the
        // crowd sizes its BFS buffers from this grid. Awake always precedes every Start.
        if (!player) player = FindAnyObjectByType<TestPlayer>();
        tileColours = new int[tiles.Length];
        for (int i = 0; i < tiles.Length; i++) { owned.Add(tiles[i].material); tileColours[i] = -1; }

        var lines = new List<Transform>();
        foreach (Transform child in transform) if (child.name == "Outline") lines.Add(child);
        outlines = lines.ToArray();

        DeriveGridMetrics();
    }

    void Start()
    {
        foreach (var platform in platforms) owned.Add(platform.material);
        SetPlatform(true);
        foreach (var tile in tiles) tile.material.color = black;

        RegisterParticipants();
        if (player && spawnPoint) player.Respawn(spawnPoint.position);
        StartCoroutine(Rounds());
    }

    /// <summary>
    /// Derive the grid from the tiles themselves rather than assuming 5x5, so regenerating the
    /// map at a different size through MapBuilder does not silently break the crowd.
    /// </summary>
    void DeriveGridMetrics()
    {
        for (int c = 0; c < _tilesByColour.Length; c++) _tilesByColour[c] = new List<Vector2Int>();

        _gridW = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(tiles.Length)));
        _gridH = Mathf.Max(1, tiles.Length / _gridW);
        _cellOrigin = tiles[0].transform.position;

        if (_gridW > 1)
        {
            Vector3 step = tiles[1].transform.position - tiles[0].transform.position;
            float s = new Vector2(step.x, step.z).magnitude;
            if (s > 0.01f) _spacing = s;
        }

        // Standing height and lip distance must be read while every tile is present, because a
        // dropped tile's collider reports a degenerate box.
        float halfTile = Mathf.Abs(tiles[0].bounds.extents.x);
        _standY = tiles[0].bounds.max.y;
        _halfX = (_gridW - 1) * 0.5f * _spacing + halfTile;
        _halfZ = (_gridH - 1) * 0.5f * _spacing + halfTile;

        _ready = true;
    }

    /// <summary>
    /// Anyone carrying a TileParticipant competes. A scene with none (or with only the solo player)
    /// still works: the player is wrapped automatically.
    ///
    /// Public because a crowd spawned after this runs needs to be added to the round; the map is the
    /// only thing that knows the participant list, so the spawner asks it rather than reaching in.
    /// Lives are only handed out to participants that have never been given any, so calling this
    /// again mid-game does not resurrect anyone's lives.
    /// </summary>
    public void RegisterParticipants()
    {
        participants.Clear();
        participants.AddRange(FindObjectsByType<TileParticipant>(FindObjectsInactive.Include));

        if (participants.Count == 0 && player != null)
        {
            var wrapped = player.gameObject.AddComponent<TileParticipant>();
            participants.Add(wrapped);
        }

        foreach (var p in participants)
            if (p != null && p.Lives <= 0) p.Lives = startingLives;
    }

    /// <summary>
    /// Where a participant waits for a round to start: spread around all four edge platforms rather
    /// than piled onto one.
    ///
    /// Packing everyone onto the south platform puts them ~0.8 apart on a 2-unit-wide ledge, which
    /// is tight enough that the shared-hazard squeeze fires before the round has even begun and
    /// bleeds the crowd away for nothing. Four sides give roughly four times the room.
    ///
    /// Uses the transform's scale rather than collider bounds: EdgeSpawnPosition can be called while
    /// the platforms are hidden, and a disabled collider reports a degenerate box.
    /// </summary>
    public Vector3 EdgeSpawnPosition(int index, int total)
    {
        if (platforms == null || platforms.Length == 0)
            return spawnPoint != null ? spawnPoint.position : Vector3.zero;

        int sides = platforms.Length;
        int side = ((index % sides) + sides) % sides;
        int idx = Mathf.Max(0, index / sides);
        int countOnSide = Mathf.Max(1, Mathf.CeilToInt(total / (float)sides));

        var platform = platforms[side];
        if (platform == null) return spawnPoint != null ? spawnPoint.position : Vector3.zero;

        Vector3 centre = platform.transform.position;
        Vector3 size = platform.transform.lossyScale;

        float t = countOnSide > 1 ? idx / (float)(countOnSide - 1) - 0.5f : 0f;
        bool longInX = size.x >= size.z;
        float half = Mathf.Max(0f, (longInX ? size.x : size.z) * 0.5f - 0.9f);
        float along = Mathf.Clamp(t * 2f * half, -half, half);

        // Sit just above the deck; agents are snapped onto the navmesh by the spawner anyway.
        var pos = new Vector3(centre.x, centre.y + size.y * 0.5f + 0.5f, centre.z);
        if (longInX) pos.x += along; else pos.z += along;
        return pos;
    }

    IEnumerator Countdown(float seconds)
    {
        Remaining = seconds;
        while (Remaining > 0) { yield return null; Remaining = Mathf.Max(0, Remaining - Time.deltaTime); }
    }

    void SetPlatform(bool visible)
    {
        foreach (var p in platforms)
        {
            if (p == null) continue;
            p.gameObject.SetActive(visible);
            var col = p.GetComponent<Collider>();
            if (col != null) col.enabled = visible;
            var c = p.material.color; c.a = 1; p.material.color = c;
        }
    }

    /// <summary>Light a participant-scaled number of tiles and deal everyone a colour.</summary>
    void Reveal()
    {
        for (int i = 0; i < tiles.Length; i++) { tileColours[i] = -1; tiles[i].material.color = black; }

        var active = ActiveParticipants();
        int litTarget = Mathf.Clamp(Mathf.RoundToInt(active.Count * litTilesPerParticipant), minLitTiles, tiles.Length);

        var order = new List<int>();
        for (int i = 0; i < tiles.Length; i++) order.Add(i);
        Shuffle(order);

        // Round-robin over shuffled tiles keeps the four colours balanced, so every colour is
        // reachable and no colour is handed a single hopeless tile.
        for (int k = 0; k < litTarget; k++)
        {
            int tile = order[k];
            int colour = k % colours.Length;
            tileColours[tile] = colour;
            tiles[tile].material.color = colours[colour];
        }

        Shuffle(active);
        for (int i = 0; i < active.Count; i++) active[i].Colour = i % colours.Length;

        RebuildColourIndex();
        CycleAdvanced?.Invoke(RoundNumber);
    }

    void RebuildColourIndex()
    {
        for (int c = 0; c < _tilesByColour.Length; c++) _tilesByColour[c].Clear();
        for (int i = 0; i < tileColours.Length; i++)
        {
            if (tileColours[i] < 0 || tileColours[i] >= _tilesByColour.Length) continue;
            _tilesByColour[tileColours[i]].Add(new Vector2Int(i % _gridW, i / _gridW));
        }
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    List<TileParticipant> ActiveParticipants()
    {
        var active = new List<TileParticipant>();
        foreach (var p in participants) if (p != null && p.Alive) active.Add(p);
        return active;
    }

    // Outlines are separate scene objects, so hide those over missing black tiles too.
    void SetBlackTiles(bool visible)
    {
        for (int i = 0; i < tiles.Length; i++) if (tileColours[i] < 0) tiles[i].gameObject.SetActive(visible);
        foreach (var line in outlines)
        {
            bool show = true;
            for (int i = 0; i < tiles.Length; i++)
            {
                Vector3 d = line.position - tiles[i].transform.position;
                if (Mathf.Abs(d.x) <= 1.01f && Mathf.Abs(d.z) <= 1.01f) { show = visible || tileColours[i] >= 0; break; }
            }
            line.gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// Which tile a participant is standing on, or -1. Tiles that are already dropped do not count,
    /// so a body hovering over a vanished tile reads as "not on a tile".
    /// </summary>
    int TileUnder(TileParticipant p)
    {
        if (p == null) return -1;
        Vector3 feet = p.Feet;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (!tiles[i].gameObject.activeSelf) continue;
            Bounds b = tiles[i].GetComponent<Collider>().bounds;
            if (feet.x >= b.min.x && feet.x <= b.max.x && feet.z >= b.min.z && feet.z <= b.max.z && feet.y >= b.max.y - .2f && feet.y <= b.max.y + 4)
                return i;
        }
        return -1;
    }

    /// <summary>Respawn the fallen onto the edge platform; survivors stay exactly where they are.</summary>
    void RespawnOutParticipants()
    {
        var waiting = new List<TileParticipant>();
        foreach (var p in participants) if (p != null && p.OutThisRound && !p.Eliminated) waiting.Add(p);

        for (int i = 0; i < waiting.Count; i++)
            waiting[i].RespawnAt(EdgeSpawnPosition(i, waiting.Count));
    }

    IEnumerator Rounds()
    {
        Phase = "All tiles black — get ready";
        yield return Countdown(initialSeconds);

        while (!GameOver)
        {
            RoundNumber++;
            SetPlatform(true);
            RespawnOutParticipants();

            Reveal();
            Phase = "Reach your target colour";
            MovementAllowed = true;
            yield return Countdown(moveSeconds);
            MovementAllowed = false;

            SetPlatform(false);
            SetBlackTiles(false);
            Phase = "Black tiles dropped";
            yield return Countdown(dropSeconds);

            // Judge the tile each participant is standing on. No tile at all is dealt with by the
            // drop itself (they fall); a lit tile of the wrong colour is thrown off the edge.
            foreach (var p in participants)
            {
                if (p == null || !p.Alive) continue;
                p.TileIndex = TileUnder(p);
                if (p.TileIndex >= 0 && tileColours[p.TileIndex] != p.Colour)
                    p.Eject(transform.position);
            }

            Phase = "Wrong colours launch — survive";
            yield return Countdown(resolveSeconds);

            // Settle anyone still alive but no longer over a tile. This also guarantees a body
            // mid-fall is never rescued by the black tiles coming back.
            foreach (var p in participants)
            {
                if (p == null || !p.Alive) continue;
                if (TileUnder(p) < 0) p.LoseLife();
            }

            CheckGameOver();

            SetBlackTiles(true);
            Phase = "Tiles returning to black";
            float elapsed = 0;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i].material.color = tileColours[i] < 0 ? black : Color.Lerp(colours[tileColours[i]], black, Mathf.Clamp01(elapsed / fadeSeconds));
                yield return null;
            }

            yield return null;
        }

        // Ending mid-round leaves the board half dismantled (platforms gone, black tiles hidden),
        // which reads as a bug against the game-over message. Put it back.
        SetPlatform(true);
        SetBlackTiles(true);
        for (int i = 0; i < tiles.Length; i++) tiles[i].material.color = black;
        MovementAllowed = false;
        Phase = "Out of lives — game over.  R to restart";
        Remaining = 0;
    }

    void CheckGameOver()
    {
        if (player == null) return;

        var own = player.GetComponent<TileParticipant>();
        if (own != null && own.Eliminated) GameOver = true;
    }

    int AliveCount()
    {
        int n = 0;
        foreach (var p in participants) if (p != null && p.Alive) n++;
        return n;
    }

    // ------------------------------------------------------------------ IArena

    public bool Ready => _ready;
    public int Width => _gridW;
    public int Height => _gridH;

    public int LitTileCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < tileColours.Length; i++) if (tileColours[i] >= 0) n++;
            return n;
        }
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _gridW && y < _gridH;

    /// <summary>
    /// The cell a world position falls in, which may be OUT OF BOUNDS.
    ///
    /// Deliberately not clamped. Clamping looks harmless but silently lies: a body standing on the
    /// edge platform, well outside the grid, resolves to the outermost row and then believes it is
    /// standing on whatever tile happens to be there - so an agent would decide it was already safe
    /// and never move. Callers that need a real cell must check InBounds or clamp for their purpose.
    /// </summary>
    public Vector2Int WorldToCell(Vector3 world)
    {
        int x = Mathf.RoundToInt((world.x - _cellOrigin.x) / _spacing);
        int y = Mathf.RoundToInt((world.z - _cellOrigin.z) / _spacing);
        return new Vector2Int(x, y);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        Vector3 p = tiles[Index(cell)].transform.position;
        return new Vector3(p.x, _standY, p.z);
    }

    public Vector2Int RandomInteriorCell(int margin)
    {
        int lo = Mathf.Clamp(margin, 0, Mathf.Min(_gridW, _gridH) / 2 - 1);
        return new Vector2Int(Random.Range(lo, _gridW - lo), Random.Range(lo, _gridH - lo));
    }

    public bool IsLit(Vector2Int cell) => InBounds(cell.x, cell.y) && tileColours[Index(cell)] >= 0;

    /// <summary>Only meaningful for an in-bounds cell; -1 means "no colour here".</summary>
    public ColorId ColourOf(Vector2Int cell) => IsLit(cell) ? (ColorId)tileColours[Index(cell)] : (ColorId)(-1);

    public IReadOnlyList<Vector2Int> TilesOfColor(ColorId c) => _tilesByColour[(int)c];

    public Vector3 PlatformCenter => transform.position;

    public float DistanceToEdge(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return Mathf.Min(_halfX - Mathf.Abs(local.x), _halfZ - Mathf.Abs(local.z));
    }

    public void BfsFrom(Vector2Int from, int[,] dst)
    {
        for (int x = 0; x < _gridW; x++)
            for (int y = 0; y < _gridH; y++)
                dst[x, y] = -1;

        if (!_ready || !InBounds(from.x, from.y)) return;

        var q = new Queue<Vector2Int>();
        dst[from.x, from.y] = 0;
        q.Enqueue(from);

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int nd = dst[c.x, c.y] + 1;
            for (int i = 0; i < 8; i++)
            {
                int nx = c.x + StepX[i], ny = c.y + StepY[i];
                if (!InBounds(nx, ny)) continue;
                if (dst[nx, ny] >= 0) continue;
                if (i >= 4 && (!InBounds(c.x + StepX[i], c.y) || !InBounds(c.x, c.y + StepY[i]))) continue;
                dst[nx, ny] = nd;
                q.Enqueue(new Vector2Int(nx, ny));
            }
        }
    }

    int Index(Vector2Int cell) => cell.y * _gridW + cell.x;

    void OnDestroy() { foreach (var m in owned) if (m) Destroy(m); }

    void OnGUI()
    {
        var own = player != null ? player.GetComponent<TileParticipant>() : null;

        GUI.Box(new Rect(18, 18, 520, 150), "FLOATING TILES — ROUND " + RoundNumber);
        GUI.Label(new Rect(32, 43, 495, 25), Phase + (Remaining > 0 ? "  " + Mathf.CeilToInt(Remaining) + "s" : ""));

        int colour = own != null ? own.Colour : -1;
        if (RoundNumber > 0 && colour >= 0)
        {
            Color old = GUI.color; GUI.color = colours[colour];
            GUI.Label(new Rect(32, 68, 490, 25), "YOUR TILE: " + names[colour]); GUI.color = old;
        }

        if (own != null)
            GUI.Label(new Rect(32, 93, 495, 25),
                "Lives: " + own.Lives + (own.OutThisRound ? "   — fell this round, back on the edge next round" : "")
                + (own.Eliminated ? "   — ELIMINATED" : ""));

        GUI.Label(new Rect(32, 116, 495, 25), "Still alive: " + AliveCount() + " / " + participants.Count + "   (lit tiles this round: " + LitCount() + ")");
        GUI.Label(new Rect(32, 139, 495, 22), "WASD / arrows: move   Space: jump   R: restart");
    }

    int LitCount()
    {
        int n = 0;
        for (int i = 0; i < tileColours.Length; i++) if (tileColours[i] >= 0) n++;
        return n;
    }
}
