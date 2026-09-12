using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloatingMap : MonoBehaviour
{
    public const string GameTitle = "Colour Me Surprised!";
    public bool IsGameOver => HasStarted && player && player.IsEliminated;
    static bool restartIntoGame;
    bool colourHazard;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { restartIntoGame = false; }
    public void RestartGame(bool skipTitle)
    {
        restartIntoGame = skipTitle;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
    public bool HasStarted { get; private set; }
    [Min(0)] public float initialSeconds = 10;
    [Min(0)] public float moveSeconds = 15;
    [Min(0)] public float dropSeconds = 1;
    [Min(2)] public float resolveSeconds = 4;
    [Min(.01f)] public float fadeSeconds = 1;
    [Range(3, 4)] public int litTileCount = 4;
    [Range(0, 15)] public int botCount = 15;
    [Tooltip("Zero gives a different match each time; another value reproduces a match.")]
    public int randomSeed;
    public Transform spawnPoint;
    public Renderer[] tiles;
    public Renderer[] platforms;
    public Player player;
    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    public bool Revealed { get; private set; }
    public bool Resolving { get; private set; }
    public bool GameOver { get; private set; }
    public int TargetColour => Human ? Human.ColourIndex : -1;
    public int RoundNumber { get; private set; }
	public float currentMoveSeconds { get; private set; }
    public TileNavigation Navigation { get; private set; }
    public IReadOnlyList<TileActor> Actors => actors;
    public IReadOnlyList<TileBot> Bots => bots;
    public TileActor Human { get; private set; }
    public static readonly Color[] Colours = { new Color(.05f,.35f,1), new Color(1,.08f,.12f), new Color(1,.85f,.02f), new Color(.05f,1,.3f) };
    public static readonly string[] ColourNames = { "BLUE", "RED", "YELLOW", "GREEN" };
    readonly List<TileActor> actors = new List<TileActor>();
    readonly List<TileBot> bots = new List<TileBot>();
    readonly List<Material> owned = new List<Material>();
    readonly List<Vector3> spawns = new List<Vector3>();
    Material[] tileMaterials, platformMaterials;
    int[] tileColours;
    Transform[] outlines;
    System.Random random;
    bool showDebug;

    void Awake()
    {
        if (!GetComponent<TitleScreen>()) gameObject.AddComponent<TitleScreen>();
    }
    void Start()
    {
        if (tiles == null || tiles.Length < 3 || platforms == null || platforms.Length == 0)
        {
            Debug.LogError("FloatingMap needs at least three tiles and a starting platform.");
            enabled = false;
            return;
        }
        random = new System.Random(randomSeed == 0 ? System.Environment.TickCount : randomSeed);
        tileMaterials = new Material[tiles.Length];
        tileColours = new int[tiles.Length];
        var lines = new List<Transform>();
        foreach (Transform child in transform) if (child.name == "Outline") lines.Add(child);
        outlines = lines.ToArray();
        for (int i = 0; i < tiles.Length; i++) { tileMaterials[i] = tiles[i].material; owned.Add(tileMaterials[i]); }
        platformMaterials = new Material[platforms.Length];
        for (int i = 0; i < platforms.Length; i++) { platformMaterials[i] = platforms[i].material; owned.Add(platformMaterials[i]); }
        if (!player) player = FindFirstObjectByType<Player>();
        if (player)
        {
            Human = player.Actor;
            Human.Map = this;
            actors.Add(Human);
        }
        string[] names = { "Pip", "Tank", "Scout", "Moss", "Brick", "Dash", "Fern", "Bash", "Wren", "Ash", "Boulder", "Rook", "Clover", "Rocky", "Finch" };
        for (int i = 0; i < Mathf.Clamp(botCount, 0, 15); i++)
        {
            var go = new GameObject(names[i]);
            go.transform.SetParent(transform);
            var actor = go.AddComponent<TileActor>();
            actor.DisplayName = names[i];
            actor.Map = this;
            if (player) actor.ConfigureMovement(player.speed, player.jumpHeight, player.launchSpeed, player.launchUpSpeed);
            actors.Add(actor);
            var bot = go.AddComponent<TileBot>();
            bot.Initialize((BotPreset)(i % 3), random);
            bots.Add(bot);
        }
        BuildSpawns();
        foreach (var tile in tiles) tile.material.color = new Color(.015f,.018f,.025f);
        if (player && spawns.Count > 0) player.Respawn(spawns[0]);
        if (restartIntoGame) { restartIntoGame = false; BeginGame(); }
    }
    public void BeginGame()
    {
        if (HasStarted) return;
        HasStarted = true;
        if (player) player.ControlsEnabled = true;
        StartCoroutine(Rounds());
    }
    void BuildSpawns()
    {
        float y = platforms[0].bounds.max.y + TileActor.Height * .5f + .035f;
        Vector3 humanSpawn = spawnPoint ? spawnPoint.position : new Vector3(0, y, -6.4f);
        if (player)
        {
            var controller = player.GetComponent<CharacterController>();
            Vector3 centreOffset = player.transform.TransformVector(controller.center);
            float halfHeight = controller.height * Mathf.Abs(player.transform.lossyScale.y) * .5f;
            humanSpawn = new Vector3(humanSpawn.x, platforms[0].bounds.max.y + halfHeight + .035f, humanSpawn.z) - centreOffset;
        }
        spawns.Add(humanSpawn);
        // Alternating sides produces crossing traffic immediately without overlapping spawns.
        Vector3[] positions = {
            new Vector3(-3, y, 6.4f), new Vector3(3, y, -6.4f), new Vector3(-6.4f, y, 2.2f),
            new Vector3(6.4f, y, -2.2f), new Vector3(3, y, 6.4f), new Vector3(-3, y, -6.4f),
            new Vector3(6.4f, y, 2.2f), new Vector3(-6.4f, y, -2.2f), new Vector3(0, y, 6.4f),
            new Vector3(-6.4f, y, 0), new Vector3(6.4f, y, 0),
            new Vector3(-5, y, -6.4f), new Vector3(5, y, -6.4f),
            new Vector3(-5, y, 6.4f), new Vector3(5, y, 6.4f)
        };
        spawns.AddRange(positions);
    }
    public void BeginRound()
    {
        RoundNumber++;
		currentMoveSeconds = Mathf.Max(3f, moveSeconds - (RoundNumber - 1));
        Revealed = Resolving = false;
        Phase = "All tiles black — get ready";
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].gameObject.SetActive(true);
            tileMaterials[i].color = new Color(.015f,.018f,.025f);
            tileColours[i] = -1;
        }
        foreach (var line in outlines) line.gameObject.SetActive(true);
        for (int i = 0; i < platforms.Length; i++)
        {
            platforms[i].gameObject.SetActive(true);
            platforms[i].GetComponent<Collider>().enabled = true;
            Color colour = platformMaterials[i].color; colour.a = 1; platformMaterials[i].color = colour;
        }
        for (int i = 0; i < actors.Count; i++)
        {
            if (RoundNumber == 1 || actors[i].IsDead) actors[i].ResetRound(spawns[i]);
            actors[i].ClearAssignment();
        }
        foreach (var bot in bots) bot.ResetRound();
        Navigation = new TileNavigation(this);
    }
    public void RevealRound()
    {
        if (Revealed) return;
        var shuffledTiles = new List<int>();
        for (int i = 0; i < tiles.Length; i++) shuffledTiles.Add(i);
        Shuffle(shuffledTiles);
        int colourCount = Mathf.Min(Mathf.Clamp(litTileCount, 3, 4), tiles.Length);
        var order = new List<int>();
        for (int i = 0; i < actors.Count; i++) if (!actors[i].IsEliminated) order.Add(i);
        Shuffle(order);
        var occupancy = new int[colourCount];
        for (int i = 0; i < colourCount; i++)
        {
            tileMaterials[shuffledTiles[i]].color = Colours[i];
            tileColours[shuffledTiles[i]] = i;
        }
        for (int i = 0; i < order.Count; i++)
        {
            int colour = i % colourCount;
            var tile = tiles[shuffledTiles[colour]];
            int slot = occupancy[colour]++;
            Vector3 destination = tile.bounds.center;
            destination.y = tile.bounds.max.y;
            // Start at opposite corners, leaving space for original-size characters.
            destination.x += (slot % 2 == 0 ? -.54f : .54f);
            destination.z += (slot == 0 || slot == 3 ? .54f : -.54f);
            actors[order[i]].Assign(colour, tile, destination, Colours[colour]);
        }
        Revealed = true;
        Remaining = moveSeconds;
        Phase = "Match your character to a tile";
        foreach (var bot in bots) bot.Reveal();
    }
    public void DropBlackTiles()
    {
        if (Resolving) return;
        Resolving = true;
        colourHazard = true;
        Remaining = 0;
        Phase = "Black tiles dropped";
        foreach (var platform in platforms) platform.gameObject.SetActive(false);
        SetBlackTiles(false);
    }
    void SetBlackTiles(bool visible)
    {
        for (int i = 0; i < tiles.Length; i++) if (tileColours[i] < 0) tiles[i].gameObject.SetActive(visible);
        foreach (var line in outlines)
        {
            bool show = visible;
            if (!visible) for (int i = 0; i < tiles.Length; i++)
            {
                Vector3 d = line.position - tiles[i].transform.position;
                if (Mathf.Abs(d.x) <= 1.01f && Mathf.Abs(d.z) <= 1.01f) { show = tileColours[i] >= 0; break; }
            }
            line.gameObject.SetActive(show);
        }
    }
    public int TileUnderActor(TileActor actor)
    {
        if (actor.IsDead) return -1;
        Vector3 feet = actor.Feet;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (!tiles[i].gameObject.activeSelf) continue;
            Bounds b = tiles[i].GetComponent<Collider>().bounds;
            if (feet.x >= b.min.x && feet.x <= b.max.x && feet.z >= b.min.z && feet.z <= b.max.z
                && feet.y >= b.max.y - .2f && feet.y <= b.max.y + 4) return i;
        }
        return -1;
    }
    public void JudgeColours()
    {
        foreach (var actor in actors)
        {
            if (actor.IsDead || actor.IsLaunched) continue;
            int tile = TileUnderActor(actor);
            if (tile >= 0 && tileColours[tile] != actor.ColourIndex) actor.LaunchOff(transform.position);
        }
        Phase = "Wrong colours launch — survive";
    }
    public void FinishRound()
    {
        foreach (var actor in actors)
        {
            if (actor.IsDead) continue;
            int tile = TileUnderActor(actor);
            if (tile < 0) actor.Die("Life lost");
            else actor.MarkSafe();
        }
        if ((Human && Human.IsEliminated) || actors.TrueForAll(a => a.IsEliminated))
        {
            GameOver = true;
            Phase = "Game over";
            Remaining = 0;
        }
    }
    void Shuffle(List<int> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = values[i]; values[i] = values[j]; values[j] = temp;
        }
    }
    IEnumerator Countdown(float seconds)
    {
        Remaining = seconds;
        while (Remaining > 0) { yield return null; Remaining = Mathf.Max(0, Remaining - Time.deltaTime); }
    }
    IEnumerator Rounds()
    {
        BeginRound();
        yield return Countdown(initialSeconds);
        while (!GameOver)
        {
            RevealRound();
            yield return Countdown(currentMoveSeconds);
            DropBlackTiles();
            yield return Countdown(dropSeconds);
            JudgeColours();
            yield return Countdown(resolveSeconds);
            // Let late launches finish; bound this wait in case wall geometry traps an actor.
            float launchWait = 0;
            while (actors.Exists(a => a.IsLaunched && !a.IsDead) && launchWait < 6)
            {
                launchWait += Time.deltaTime;
                yield return null;
            }
            foreach (var actor in actors) if (actor.IsLaunched && !actor.IsDead) actor.Die("Wrong colour!");
            colourHazard = false;
            FinishRound();
            if (GameOver) yield break;
            SetBlackTiles(true);
            Phase = "Tiles returning to black";
            float elapsed = 0;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < tiles.Length; i++)
                {
                    Color black = new Color(.015f,.018f,.025f);
                    tileMaterials[i].color = tileColours[i] < 0 ? black
                        : Color.Lerp(Colours[tileColours[i]], black, Mathf.Clamp01(elapsed / fadeSeconds));
                }
                yield return null;
            }
            BeginRound();
        }
    }
    void Update()
    {
        if (IsGameOver && !GameOver)
        {
            GameOver = true;
            colourHazard = false;
            StopAllCoroutines();
            Phase = "Game over";
            Remaining = 0;
        }
        if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
        if (Input.GetKeyDown(KeyCode.N)) Remaining = 0;
    }
    void LateUpdate()
    {
        if (Navigation == null || GameOver || IsGameOver) return;
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        if (player) foreach (var actor in actors)
            actor.ConfigureMovement(player.speed, player.jumpHeight, player.launchSpeed, player.launchUpSpeed);
        foreach (var bot in bots) if (bot.isActiveAndEnabled) bot.Tick(dt);
        foreach (var actor in actors) actor.Step(dt);
        for (int i = 0; i < actors.Count; i++) for (int j = i + 1; j < actors.Count; j++)
            TileActor.ResolveContact(actors[i], actors[j], dt);
        if (colourHazard) JudgeColours();
    }
    // ---- HUD ----
    // Split into separate corners rather than one block: each piece of information gets its own
    // place, so nothing has to be read as a paragraph and the things you check mid-round (timer,
    // your colour, lives) sit at the edges of vision instead of in a wall of text.

    GUIStyle _panelTitle, _panelBody, _timer;
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
        // colour because it is designed for light backgrounds, and GUI.color multiplies with it.
        // Left alone, white-on-dark panels render as near-black-on-black.
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
    }

    /// <summary>Panel with a dim caption over a brighter value.</summary>
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
        if (!HasStarted || IsGameOver) return;
        EnsureHudStyles();

        int alive = 0;
        foreach (var actor in actors) if (actor != null && !actor.IsDead) alive++;

        int lit = 0;
        if (tileColours != null) foreach (var c in tileColours) if (c >= 0) lit++;

        int colour = TargetColour;
        bool revealed = Revealed && colour >= 0 && colour < ColourNames.Length;

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
              alive + " / " + actors.Count + "     " + lit + " lit", Color.white);

        // 4. bottom right - lives, with a pip each
        int lives = player != null ? player.LivesRemaining : 0;
        var chip = new Rect(Screen.width - 740f, Screen.height - 178f, 712f, 150f);
        Panel(chip, "LIVES", lives <= 0 ? "ELIMINATED" : lives.ToString(), Color.white);
        for (int i = 0; i < 3; i++)
        {
            GUI.color = i < lives ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(chip.x + chip.width - 190f + i * 58f, chip.y + 74f, 44f, 44f), Px);
        }
        GUI.color = Color.white;

        // 5. bottom left - your colour, as a chip you cannot misread
        var colourPanel = new Rect(28f, Screen.height - 178f, 780f, 150f);
        Panel(colourPanel, "YOUR COLOUR", revealed ? ColourNames[colour] : "waiting",
              revealed ? Colours[colour] : Color.white);
        if (revealed)
        {
            GUI.color = Colours[colour];
            GUI.DrawTexture(new Rect(colourPanel.x + colourPanel.width - 190f, colourPanel.y + 64f, 156f, 58f), Px);
            GUI.color = Color.white;
        }

        // 6. bottom centre - controls, small and out of the way
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(0f, Screen.height - 56f, Screen.width, 52f),
                  "WASD: move   ·   Click/Enter: punch   ·   R: restart   ·   N: skip",
                  new GUIStyle(_panelTitle) { fontSize = 34, alignment = TextAnchor.MiddleCenter });
        GUI.color = Color.white;

        if (!showDebug) return;
        GUI.Box(new Rect(18,160,470,35 + bots.Count * 23), "BOT DEBUG");
        for (int i = 0; i < bots.Count; i++)
        {
            var actor = bots[i].GetComponent<TileActor>();
            GUI.Label(new Rect(32,190 + i * 23,445,23), actor.DisplayName + " · " + actor.PersonalityName
                + " · " + bots[i].State + " · lives " + actor.LivesRemaining);
        }
    }
    void OnDestroy() { foreach (var material in owned) if (material) Destroy(material); }
}
