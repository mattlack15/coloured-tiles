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
    [Min(0)] public float initialSeconds = 3;
    [Min(0)] public float moveSeconds = 15;
    [Min(0)] public float dropSeconds = 1;
    [Min(2)] public float resolveSeconds = 4;
    [Min(0.01f)] public float fadeSeconds = 1;

    [Header("Tiles")]
    [Tooltip("Lit tiles relative to participant count. Tiles can be shared, so this does not cap how many people survive - it controls how far anyone travels and how hard the crowd packs onto the few safe spots. At 0.16 with 61 participants, about 10 of the 49 tiles light up.")]
    [Range(0.05f, 2f)] public float litTilesPerParticipant = 0.16f;
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

    /// <summary>The RGB a colour index maps to. Exposed so a body can be painted the exact colour of
    /// the tiles it is hunting - starting with the player, who otherwise never shows its colour.
    /// It has to come from here rather than the shared Jam.Palette, because this arena's four RGB
    /// values are its own and a colour cue that is merely similar is not good enough in a game
    /// about matching colour.</summary>
    public Color ColourForIndex(int index) =>
        index >= 0 && index < colours.Length ? colours[index] : Color.white;

    public string Phase { get; private set; }

    [Header("Title screen")]
    public string gameTitle = "FLOATING TILES";

    /// <summary>False until Start is pressed on the title screen. Nothing runs until then.</summary>
    public bool HasStarted { get; private set; }

    public void BeginGame()
    {
        if (HasStarted) return;
        HasStarted = true;
        if (player != null) player.ControlsEnabled = true;
    }
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
        if (player && spawnPoint) player.Respawn(RandomBoardPosition());

        // The title screen owns the player until Start is pressed.
        if (player != null) player.ControlsEnabled = false;

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

        // The lip is the edge of the whole walkable floor, NOT the edge of the tile grid. The ring
        // sits flush outside the grid, so a body shoved off an edge tile just steps onto it.
        // Measuring to the grid made the squeeze hazard drop agents standing on solid ground: one
        // tile in they read as "at the lip" and were fired into the void beside a platform.
        if (platforms != null)
        {
            foreach (var platform in platforms)
            {
                if (platform == null) continue;
                Vector3 c = transform.InverseTransformPoint(platform.transform.position);
                Vector3 s = platform.transform.lossyScale;
                _halfX = Mathf.Max(_halfX, Mathf.Abs(c.x) + s.x * 0.5f);
                _halfZ = Mathf.Max(_halfZ, Mathf.Abs(c.z) + s.z * 0.5f);
            }
        }

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
        // The player always competes, whether or not a crowd registered before this ran. Gating this
        // on an empty list made it depend on script execution order: if the crowd's Start happened
        // to go first, the player was never wrapped and silently sat out the whole game - no colour,
        // no judging, no respawn.
        if (player == null) player = FindAnyObjectByType<TestPlayer>();
        if (player != null && player.GetComponent<TileParticipant>() == null)
            player.gameObject.AddComponent<TileParticipant>();

        participants.Clear();
        participants.AddRange(FindObjectsByType<TileParticipant>(FindObjectsInactive.Include));

        foreach (var p in participants)
            if (p != null && p.Lives <= 0) p.Lives = startingLives;
    }

    /// <summary>
    /// A random spot on the tile grid, used to start everyone off.
    ///
    /// Participants begin scattered across the board rather than queued on the edge ring, so a round
    /// opens with everyone already in the thick of it instead of running in from outside.
    /// </summary>
    public Vector3 RandomBoardPosition() => CellToWorld(RandomInteriorCell(0));

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
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Colour = i % colours.Length;
            active[i].ShowColour(colours[active[i].Colour]);
        }

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
            waiting[i].RespawnAt(RandomBoardPosition());
    }

    IEnumerator Rounds()
    {
        // Hold on the title screen. No round runs and nobody moves until Start is pressed.
        Phase = "Press Start";
        while (!HasStarted) yield return null;

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

    // ---- HUD ----
    // Split into separate corners rather than one big block: each piece of information gets its own
    // place, so nothing has to be read as a paragraph and the things you check mid-round (timer,
    // your colour, lives) sit at the edges of vision instead of in a wall of text.

    GUIStyle _panelTitle, _panelBody, _timer, _banner;
    static Texture2D _px;

    static Texture2D Px
    {
        get
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }
            return _px;
        }
    }

    void EnsureHudStyles()
    {
        if (_panelTitle != null) return;

        // normal.textColor must be set explicitly: GUI.skin.label ships with a DARK default text
        // colour (it is designed for light backgrounds), and GUI.color multiplies with it. Leaving
        // it alone renders white-on-dark panels as near-black-on-black.
        _panelTitle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _panelBody = new GUIStyle(GUI.skin.label)
        {
            fontSize = 56,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _timer = new GUIStyle(GUI.skin.label)
        {
            fontSize = 170,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        _banner = new GUIStyle(GUI.skin.label)
        {
            fontSize = 100,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
    }

    /// <summary>Small titled panel: a dim caption over a brighter value.</summary>
    void Panel(Rect r, string caption, string value, Color valueColour)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(r, Px);
        GUI.color = new Color(0.75f, 0.82f, 0.9f, 1f);
        GUI.Label(new Rect(r.x + 24f, r.y + 10f, r.width - 48f, 50f), caption, _panelTitle);
        GUI.color = valueColour;
        GUI.Label(new Rect(r.x + 24f, r.y + 62f, r.width - 48f, 78f), value, _panelBody);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        EnsureHudStyles();

        var own = player != null ? player.GetComponent<TileParticipant>() : null;
        int colour = own != null ? own.Colour : -1;
        bool revealed = RoundNumber > 0 && colour >= 0;

        // 1. top left - where we are in the round
        Panel(new Rect(28f, 24f, 900f, 150f), "ROUND " + RoundNumber, Phase, Color.white);

        // 2. top centre - the countdown on its own, big enough to glance at
        if (Remaining > 0f)
        {
            var timerRect = new Rect(Screen.width * 0.5f - 220f, 14f, 440f, 200f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(timerRect, Px);
            GUI.color = Remaining < 4f ? new Color(1f, 0.55f, 0.4f) : Color.white;
            GUI.Label(timerRect, Mathf.CeilToInt(Remaining).ToString(), _timer);
            GUI.color = Color.white;
        }

        // 3. top right - the state of the board
        Panel(new Rect(Screen.width - 740f, 24f, 712f, 150f), "STILL STANDING",
              AliveCount() + " / " + participants.Count + "     " + LitCount() + " lit", Color.white);

        // 4. bottom right - lives, with pips
        var chip = new Rect(Screen.width - 740f, Screen.height - 178f, 712f, 150f);
        Panel(chip, "LIVES", own != null ? own.Lives + (own.Eliminated ? "   ELIMINATED" : "") : "-", Color.white);
        if (own != null)
        {
            for (int i = 0; i < startingLives; i++)
            {
                GUI.color = i < own.Lives ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(chip.x + chip.width - 190f + i * 58f, chip.y + 74f, 44f, 44f), Px);
            }
            GUI.color = Color.white;
        }

        // 5. bottom left - your colour, as a chip you cannot misread
        var colourPanel = new Rect(28f, Screen.height - 178f, 780f, 150f);
        Panel(colourPanel, "YOUR COLOUR", revealed ? names[colour] : "waiting",
              revealed ? colours[colour] : Color.white);
        if (revealed)
        {
            GUI.color = colours[colour];
            GUI.DrawTexture(new Rect(colourPanel.x + colourPanel.width - 190f, colourPanel.y + 64f, 156f, 58f), Px);
            GUI.color = Color.white;
        }

        // 5. bottom centre - controls, small and out of the way
        string hint = InputBridge.HasGamepad
            ? "stick: move   ·   X / RT: punch   ·   Start: restart"
            : "WASD: move   ·   E: punch   ·   R: restart";
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(0f, Screen.height - 56f, Screen.width, 52f), hint,
                  new GUIStyle(_panelTitle) { fontSize = 34, alignment = TextAnchor.MiddleCenter });
        GUI.color = Color.white;

        // 6. centre - end of run, over everything else
        if (GameOver)
        {
            var box = new Rect(0f, Screen.height * 0.5f - 46f, Screen.width, 92f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(box, Px);
            GUI.color = new Color(1f, 0.5f, 0.5f);
            GUI.Label(box, "OUT OF LIVES", _banner);
            GUI.color = Color.white;
        }
    }

    int LitCount()
    {
        int n = 0;
        for (int i = 0; i < tileColours.Length; i++) if (tileColours[i] >= 0) n++;
        return n;
    }
}
