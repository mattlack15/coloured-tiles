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
public class FloatingMap : MonoBehaviour
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

    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    public int RoundNumber { get; private set; }
    public bool GameOver { get; private set; }

    readonly List<TileParticipant> participants = new List<TileParticipant>();
    public IReadOnlyList<TileParticipant> Participants => participants;

    readonly Color black = new Color(.015f, .018f, .025f);
    readonly Color[] colours = { new Color(.05f, .35f, 1), new Color(1, .08f, .12f), new Color(1, .85f, .02f), new Color(.05f, 1, .3f) };
    readonly string[] names = { "BLUE", "RED", "YELLOW", "GREEN" };

    readonly List<Material> owned = new List<Material>();
    int[] tileColours;
    Transform[] outlines;

    void Start()
    {
        if (!player) player = FindAnyObjectByType<TestPlayer>();
        tileColours = new int[tiles.Length];
        for (int i = 0; i < tiles.Length; i++) { owned.Add(tiles[i].material); tileColours[i] = -1; }

        var lines = new List<Transform>();
        foreach (Transform child in transform) if (child.name == "Outline") lines.Add(child);
        outlines = lines.ToArray();

        foreach (var platform in platforms) owned.Add(platform.material);
        SetPlatform(true);
        foreach (var tile in tiles) tile.material.color = black;

        RegisterParticipants();
        if (player && spawnPoint) player.Respawn(spawnPoint.position);
        StartCoroutine(Rounds());
    }

    /// <summary>
    /// Anyone carrying a TileParticipant competes. A scene with none (or with only the solo player)
    /// still works: the player is wrapped automatically.
    /// </summary>
    void RegisterParticipants()
    {
        participants.Clear();
        participants.AddRange(FindObjectsByType<TileParticipant>(FindObjectsInactive.Include));

        if (participants.Count == 0 && player != null)
        {
            var wrapped = player.gameObject.AddComponent<TileParticipant>();
            participants.Add(wrapped);
        }

        foreach (var p in participants) p.Lives = startingLives;
    }

    /// <summary>Spread everyone along the south edge platform. Index-ordered so they never stack.</summary>
    public Vector3 EdgeSpawnPosition(int index, int total)
    {
        float halfWidth = platforms.Length > 0 && platforms[0] != null
            ? platforms[0].bounds.extents.x - 0.8f
            : 6.5f;
        float spacing = total > 1 ? Mathf.Min(1.0f, (halfWidth * 2f) / (total - 1)) : 0f;
        float x = (index - (total - 1) * 0.5f) * spacing;
        x = Mathf.Clamp(x, -halfWidth, halfWidth);

        float z = spawnPoint != null ? spawnPoint.position.z : -6.4f;
        float y = spawnPoint != null ? spawnPoint.position.y : 1.3f;
        return new Vector3(x, y, z);
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
            yield return Countdown(moveSeconds);

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
