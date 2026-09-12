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
    public TestPlayer player;
    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    public bool Revealed { get; private set; }
    public bool Resolving { get; private set; }
    public bool GameOver { get; private set; }
    public int TargetColour => Human ? Human.ColourIndex : -1;
    public int RoundNumber { get; private set; }
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
        if (!player) player = FindFirstObjectByType<TestPlayer>();
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
        if (player && spawnPoint) player.Respawn(spawnPoint.position);
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
            yield return Countdown(moveSeconds);
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
    void OnGUI()
    {
        if (!HasStarted || IsGameOver) return;
        GUI.Box(new Rect(18,18,470,125), "FLOATING TILES — ROUND " + RoundNumber);
        GUI.Label(new Rect(32,43,445,25), Phase + (Remaining > 0 ? "  " + Mathf.CeilToInt(Remaining) + "s" : ""));
        GUI.Label(new Rect(32,93,445,25), "WASD / arrows: move   Space: jump   R: restart   N: skip timer");
        if (player) GUI.Label(new Rect(32,116,445,25), "Lives: " + player.LivesRemaining + " / 3");
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
