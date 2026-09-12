using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloatingMap : MonoBehaviour
{
    [Min(0)] public float initialSeconds = 10;
    [Min(.1f)] public float moveSeconds = 15;
    [Min(0)] public float dropSeconds = 1;
    [Min(2)] public float resolveSeconds = 4;
    [Min(.01f)] public float fadeSeconds = 1;
    [Range(3, 4)] public int litTileCount = 4;
    [Range(0, 11)] public int botCount = 7;
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
    public static readonly Color[] Colours = { new Color(.08f,.45f,1), new Color(1,.16f,.22f), new Color(1,.85f,.08f), new Color(.1f,1,.4f) };
    public static readonly string[] ColourNames = { "BLUE", "RED", "YELLOW", "GREEN" };
    readonly List<TileActor> actors = new List<TileActor>();
    readonly List<TileBot> bots = new List<TileBot>();
    readonly List<Material> owned = new List<Material>();
    readonly List<Vector3> spawns = new List<Vector3>();
    Material[] tileMaterials, platformMaterials;
    int[] tileColours;
    Transform[] outlines;
    System.Random random;
    Camera gameCamera;
    bool showDebug;
    GUIStyle titleStyle, textStyle, smallStyle, labelStyle;

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
        string[] names = { "Pip", "Tank", "Scout", "Moss", "Brick", "Dash", "Fern", "Bash", "Wren", "Ash", "Boulder" };
        for (int i = 0; i < Mathf.Clamp(botCount, 0, 11); i++)
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
        gameCamera = Camera.main;
        if (gameCamera)
        {
            gameCamera.transform.position = new Vector3(0, 15, -11);
            gameCamera.transform.LookAt(Vector3.zero);
            gameCamera.orthographic = true;
            gameCamera.orthographicSize = 8.6f;
        }
        StartCoroutine(Rounds());
    }
    void BuildSpawns()
    {
        float y = platforms[0].bounds.max.y + TileActor.Height * .5f + .035f;
        Vector3 humanSpawn = spawnPoint ? spawnPoint.position : new Vector3(0, y, -6.4f);
        humanSpawn.y = y;
        spawns.Add(humanSpawn);
        // Alternating sides produces crossing traffic immediately without overlapping spawns.
        Vector3[] positions = {
            new Vector3(-3, y, 6.4f), new Vector3(3, y, -6.4f), new Vector3(-6.4f, y, 2.2f),
            new Vector3(6.4f, y, -2.2f), new Vector3(3, y, 6.4f), new Vector3(-3, y, -6.4f),
            new Vector3(6.4f, y, 2.2f), new Vector3(-6.4f, y, -2.2f), new Vector3(0, y, 6.4f),
            new Vector3(-6.4f, y, 0), new Vector3(6.4f, y, 0)
        };
        spawns.AddRange(positions);
    }
    public void BeginRound()
    {
        RoundNumber++;
        Revealed = Resolving = false;
        Phase = "Get ready";
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].gameObject.SetActive(true);
            tileMaterials[i].color = new Color(.025f,.035f,.055f);
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
            // Four non-overlapping anchors fit the 2m tiles with the shared .28m radius.
            destination.x += (slot % 2 == 0 ? -.42f : .42f);
            destination.z += (slot / 2 == 0 ? .42f : -.42f);
            actors[order[i]].Assign(colour, tile, destination, Colours[colour]);
        }
        Revealed = true;
        Remaining = moveSeconds;
        Phase = "Find your colour";
        foreach (var bot in bots) bot.Reveal();
    }
    public void DropBlackTiles()
    {
        if (Resolving) return;
        Resolving = true;
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
            if (tile < 0 || actor.IsLaunched || tileColours[tile] != actor.ColourIndex) actor.Die("Life lost");
            else actor.MarkSafe();
        }
        if ((Human && Human.IsEliminated) || actors.TrueForAll(a => a.IsEliminated))
        {
            GameOver = true;
            Phase = "Game over — R to restart";
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
                    Color black = new Color(.025f,.035f,.055f);
                    tileMaterials[i].color = tileColours[i] < 0 ? black
                        : Color.Lerp(Colours[tileColours[i]], black, Mathf.Clamp01(elapsed / fadeSeconds));
                }
                yield return null;
            }
            BeginRound();
        }
    }
    void Update() { if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug; }
    void FixedUpdate()
    {
        if (Navigation == null) return;
        float dt = Time.fixedDeltaTime;
        foreach (var bot in bots) if (bot.isActiveAndEnabled) bot.Tick(dt);
        foreach (var actor in actors) actor.Step(dt);
        for (int i = 0; i < actors.Count; i++) for (int j = i + 1; j < actors.Count; j++)
            TileActor.ResolveContact(actors[i], actors[j], dt);
    }
    void Styles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 17 };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }
    void OnGUI()
    {
        if (!gameCamera) return;
        Styles();
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float width = Screen.width / scale, height = Screen.height / scale;
        GUI.Box(new Rect(20, 20, 350, 152), "");
        GUI.Label(new Rect(34, 28, 320, 32), "COLOURED TILES  /  " + RoundNumber, titleStyle);
        GUI.Label(new Rect(34, 66, 320, 26), Phase, textStyle);
        string prompt = GameOver ? "R to restart with 3 lives" : Resolving ? (Human && Human.IsDead ? "Life lost · next round respawn" : "Stay on your colour")
            : Revealed && Human ? "Reach " + ColourNames[Human.ColourIndex] + "  ·  " + Remaining.ToString("0.0") + "s"
            : "Colour reveal in " + Mathf.CeilToInt(Remaining) + "s";
        GUI.color = Revealed && Human && !Resolving ? Colours[Human.ColourIndex] : Color.white;
        GUI.Label(new Rect(34, 99, 320, 30), prompt, titleStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(34, 137, 320, 23), "WASD / arrows  ·  Space: jump  ·  R: restart", smallStyle);
        GUI.Box(new Rect(width - 245, 20, 225, 50 + actors.Count * 25), "");
        GUI.Label(new Rect(width - 230, 29, 210, 25), "ROSTER  /  LIVES", smallStyle);
        for (int i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            GUI.color = actor.IsDead ? Color.gray : actor.ColourIndex < 0 ? Color.white : Colours[actor.ColourIndex];
            string name = actor.DisplayName + (actor.PersonalityName.Length > 0 ? " · " + actor.PersonalityName : "");
            GUI.Label(new Rect(width - 230, 57 + i * 25, 185, 23), name, smallStyle);
            GUI.Label(new Rect(width - 60, 57 + i * 25, 40, 23), actor.LivesRemaining + "/3", smallStyle);
        }
        GUI.color = Color.white;
        foreach (var actor in actors)
        {
            if (actor.Feet.y < -2) continue;
            Vector3 screen = gameCamera.WorldToScreenPoint(actor.transform.position + Vector3.up * .9f);
            if (screen.z <= 0) continue;
            float x = screen.x / scale, y = (Screen.height - screen.y) / scale;
            GUI.Box(new Rect(x - 31, y - 10, 62, 22), "");
            GUI.Label(new Rect(x - 31, y - 10, 62, 22), actor.DisplayName, labelStyle);
        }
        if (showDebug)
        {
            GUI.Box(new Rect(20, height - 35 - bots.Count * 21, 490, 30 + bots.Count * 21), "");
            for (int i = 0; i < bots.Count; i++)
                GUI.Label(new Rect(32, height - 29 - (bots.Count - i) * 21, 470, 22),
                    bots[i].name + ": " + bots[i].State + "  |  routes " + bots[i].Replans + "  |  push ticks " + bots[i].PushDecisions, smallStyle);
        }
        GUI.matrix = previous;
    }
    void OnDestroy() { foreach (var material in owned) if (material) Destroy(material); }
}
