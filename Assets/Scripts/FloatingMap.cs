using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloatingMap : MonoBehaviour
{
    [Min(0)] public float initialSeconds = 10;
    [Min(0)] public float moveSeconds = 15;
    [Min(0)] public float dropSeconds = 1;
    [Min(2)] public float resolveSeconds = 4;
    [Min(0.01f)] public float fadeSeconds = 1;
    [Range(3, 4)] public int litTileCount = 4;
    public Transform spawnPoint;
    public Renderer[] tiles;
    public Renderer[] platforms;
    public TestPlayer player;
    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    public int RoundNumber { get; private set; }
    public int TargetColour { get; private set; }
    readonly Color black = new Color(.015f,.018f,.025f);
    readonly Color[] colours = { new Color(.05f,.35f,1), new Color(1,.08f,.12f), new Color(1,.85f,.02f), new Color(.05f,1,.3f) };
    readonly string[] names = { "BLUE", "RED", "YELLOW", "GREEN" };
    readonly List<Material> owned = new List<Material>();
    int[] tileColours;
    Transform[] outlines;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<TestPlayer>();
        tileColours = new int[tiles.Length];
        for (int i = 0; i < tiles.Length; i++) { owned.Add(tiles[i].material); tileColours[i] = -1; }
        var lines = new List<Transform>();
        foreach (Transform child in transform) if (child.name == "Outline") lines.Add(child);
        outlines = lines.ToArray();
        foreach (var platform in platforms) owned.Add(platform.material);
        SetPlatform(true);
        foreach (var tile in tiles) tile.material.color = black;
        if (player && spawnPoint) player.Respawn(spawnPoint.position);
        StartCoroutine(Rounds());
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
            p.gameObject.SetActive(visible);
            p.GetComponent<Collider>().enabled = visible;
            var c = p.material.color; c.a = 1; p.material.color = c;
        }
    }
    void Reveal()
    {
        var indices = new List<int>();
        for (int i = 0; i < tiles.Length; i++) { indices.Add(i); tileColours[i] = -1; }
        int count = Mathf.Min(Mathf.Clamp(litTileCount, 3, 4), tiles.Length);
        for (int i = 0; i < count; i++)
        {
            int pick = Random.Range(0, indices.Count), tile = indices[pick];
            tileColours[tile] = i; tiles[tile].material.color = colours[i]; indices.RemoveAt(pick);
        }
        TargetColour = Random.Range(0, count);
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
    int TileUnderPlayer()
    {
        if (!player || player.IsDead) return -1;
        // Check the tile below the feet, including a player jumping at judgment time.
        Vector3 feet = player.Feet;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (!tiles[i].gameObject.activeSelf) continue;
            Bounds b = tiles[i].GetComponent<Collider>().bounds;
            if (feet.x >= b.min.x && feet.x <= b.max.x && feet.z >= b.min.z && feet.z <= b.max.z && feet.y >= b.max.y - .2f && feet.y <= b.max.y + 4)
                return i;
        }
        return -1;
    }
    IEnumerator Rounds()
    {
        Phase = "All tiles black — get ready";
        yield return Countdown(initialSeconds);
        while (true)
        {
            RoundNumber++;
            SetPlatform(true);
            if (player && player.IsDead && spawnPoint) player.Respawn(spawnPoint.position);
            Reveal();
            Phase = "Reach your target colour";
            yield return Countdown(moveSeconds);
            SetPlatform(false);
            SetBlackTiles(false);
            Phase = "Black tiles dropped";
            yield return Countdown(dropSeconds);
            int tile = TileUnderPlayer();
            if (tile >= 0 && tileColours[tile] != TargetColour) player.LaunchOff(transform.position);
            Phase = "Wrong colours launch — survive";
            yield return Countdown(resolveSeconds);
            // Settle anyone still airborne/off-grid before restoring colliders.
            // This also guarantees no falling player is rescued by a returning black tile.
            if (player && !player.IsDead && TileUnderPlayer() < 0) player.Die();
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
        }
    }
    void OnDestroy() { foreach (var m in owned) if (m) Destroy(m); }
    void OnGUI()
    {
        GUI.Box(new Rect(18,18,470,125), "FLOATING TILES — ROUND " + RoundNumber);
        GUI.Label(new Rect(32,43,445,25), Phase + (Remaining > 0 ? "  " + Mathf.CeilToInt(Remaining) + "s" : ""));
        if (RoundNumber > 0)
        {
            Color old = GUI.color; GUI.color = colours[TargetColour];
            GUI.Label(new Rect(32,68,440,25), "YOUR TILE: " + names[TargetColour]); GUI.color = old;
        }
        GUI.Label(new Rect(32,93,445,25), "WASD / arrows: move   Space: jump   R: restart game");
        if (player) GUI.Label(new Rect(32,116,445,25), "Lives lost: " + player.LivesLost);
    }
}
