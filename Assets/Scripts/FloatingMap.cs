using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloatingMap : MonoBehaviour
{
    [Min(0)] public float initialSeconds = 10;
    [Min(0)] public float revealSeconds = 5;
    [Min(0.01f)] public float fadeSeconds = 1;
    [Range(3, 4)] public int litTileCount = 4;
    public Transform spawnPoint;
    public Renderer[] tiles;
    public Renderer[] platforms;
    public string Phase { get; private set; }
    public float Remaining { get; private set; }
    readonly Color[] colours = { new Color(.05f,.35f,1), new Color(1,.08f,.12f), new Color(1,.85f,.02f), new Color(.05f,1,.3f) };
    readonly List<Material> owned = new List<Material>();

    void Start() { StartCoroutine(Round()); }
    IEnumerator Countdown(float seconds)
    {
        Remaining = seconds;
        while (Remaining > 0) { yield return null; Remaining = Mathf.Max(0, Remaining - Time.deltaTime); }
    }
    IEnumerator Round()
    {
        foreach (var tile in tiles) { var m = tile.material; owned.Add(m); m.color = new Color(.015f,.018f,.025f); }
        Phase = "Observe the grid";
        yield return Countdown(initialSeconds);
        var indices = new List<int>();
        for (int i = 0; i < tiles.Length; i++) indices.Add(i);
        for (int i = 0; i < Mathf.Min(litTileCount, tiles.Length); i++)
        {
            int pick = Random.Range(0, indices.Count);
            tiles[indices[pick]].material.color = colours[i];
            indices.RemoveAt(pick);
        }
        Phase = "Colours revealed — move onto the tiles";
        yield return Countdown(revealSeconds);
        Phase = "Platform disappearing";
        foreach (var platform in platforms) platform.GetComponent<Collider>().enabled = false;
        var mats = new List<Material>();
        foreach (var platform in platforms) { var m = platform.material; mats.Add(m); owned.Add(m); }
        float elapsed = 0;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.deltaTime;
            foreach (var m in mats) { Color c = m.color; c.a = 1 - Mathf.Clamp01(elapsed / fadeSeconds); m.color = c; }
            yield return null;
        }
        foreach (var platform in platforms) platform.gameObject.SetActive(false);
        Phase = "Stay on the grid";
    }
    void OnDestroy() { foreach (var m in owned) if (m) Destroy(m); }
    void OnGUI()
    {
        GUI.Box(new Rect(18,18,430,95), "FLOATING TILES");
        GUI.Label(new Rect(32,43,410,25), Phase + (Remaining > 0 ? "  " + Mathf.CeilToInt(Remaining) + "s" : ""));
        GUI.Label(new Rect(32,70,410,25), "WASD / arrows: move     Space: jump     R: restart");
    }
}
