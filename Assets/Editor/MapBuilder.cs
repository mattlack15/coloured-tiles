using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;


[InitializeOnLoad]
public static class MapBuilder
{
    static MapBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (!Application.isBatchMode && !System.IO.File.Exists("Assets/Scenes/FloatingTiles.unity")) Build();
        };
    }

    static Material Mat(string name, Color color, bool transparent = false)
    {
        string path = "Assets/Generated/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;
        var m = new Material(Shader.Find(transparent ? "FloatingTiles/Fade" : "Unlit/Color"));
        m.color = color;
        AssetDatabase.CreateAsset(m,"Assets/Generated/" + name + ".mat");
        return m;
    }
    static GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent); go.transform.position = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat; return go;
    }
    [MenuItem("Floating Tiles/Create Map Scene")]
    public static void Build()
    {
        System.IO.Directory.CreateDirectory("Assets/Generated");
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var black = Mat("Black",new Color(.015f,.018f,.025f));
        var outline = Mat("Outline",new Color(.4f,.65f,.75f));
        var ring = Mat("Platform",new Color(.22f,.32f,.42f),true);
        var danger = Mat("KillPlane",new Color(.35f,.035f,.09f));
        var playerMat = Mat("Player",Color.white);
        var root = new GameObject("Floating Map"); var map = root.AddComponent<FloatingMap>();
        var tiles = new List<Renderer>(); var platforms = new List<Renderer>();
        for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
        {
            var center = new Vector3((x-2)*2.2f,0,(z-2)*2.2f);
            var tile = Box("Tile " + (z*5+x+1).ToString("00"),center,new Vector3(2,.35f,2),black,root.transform);
            tiles.Add(tile.GetComponent<Renderer>());
            for (int edge=0;edge<4;edge++)
            {
                bool alongX = edge < 2; float sign = edge % 2 == 0 ? -1 : 1;
                var pos = center + new Vector3(alongX ? 0 : sign*.97f,.19f,alongX ? sign*.97f : 0);
                var line = Box("Outline",pos,alongX ? new Vector3(2,.025f,.035f) : new Vector3(.035f,.025f,2),outline,root.transform);
                Object.DestroyImmediate(line.GetComponent<Collider>());
            }
        }
        platforms.Add(Box("Start platform — south",new Vector3(0,0,-6.4f),new Vector3(14.8f,.35f,2),ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — north",new Vector3(0,0,6.4f),new Vector3(14.8f,.35f,2),ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — west",new Vector3(-6.4f,0,0),new Vector3(2,.35f,10.8f),ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — east",new Vector3(6.4f,0,0),new Vector3(2,.35f,10.8f),ring,root.transform).GetComponent<Renderer>());
        var spawn = new GameObject("Spawn Point"); spawn.transform.position = new Vector3(0,1.3f,-6.4f); spawn.transform.SetParent(root.transform);
        map.spawnPoint = spawn.transform; map.tiles = tiles.ToArray(); map.platforms = platforms.ToArray();
        var kill = Box("Kill Plane",new Vector3(0,-7,0),new Vector3(100,1,100),danger);
        kill.GetComponent<BoxCollider>().isTrigger = true; kill.AddComponent<KillPlane>();
        var body = kill.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule); player.name = "Test Player";
        Object.DestroyImmediate(player.GetComponent<Collider>()); player.transform.position = spawn.transform.position;
        player.GetComponent<Renderer>().sharedMaterial = playerMat; player.AddComponent<CharacterController>(); player.AddComponent<TestPlayer>();
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0,18,-20); camera.transform.LookAt(Vector3.zero);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.08f); camera.fieldOfView = 52;
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/FloatingTiles.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/FloatingTiles.unity",true) };
        AssetDatabase.SaveAssets();
        Debug.Log("FLOATING_MAP_BUILD_OK: 25 tiles, 4 platforms, kill plane, test player.");
    }
}
