using System.Collections.Generic;
using Jam;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;


[InitializeOnLoad]
public static class MapBuilder
{
    // Art. Both are plain meshes with no material bound (the OBJ files declare an mtllib but use
    // no usemtl), so every surface colour here is assigned by code - which is what the game needs
    // anyway, since tiles have to be tinted per colour.
    const string TileModelPath = "Assets/Models/Tile_t2.obj";
    const string PlayerModelPath = "Assets/Models/Bob.obj";

    /// <summary>Grid size in tiles, per side.</summary>
    const int GridSize = 7;

    /// <summary>Centre-to-centre tile spacing. Must EQUAL <see cref="TileWorldSize"/> so adjacent
    /// tiles share an edge: at anything larger they share no floor, and a NavMeshAgent can never
    /// path between them because there is nothing in the gap to walk on.</summary>
    const float GridSpacing = 2f;

    /// <summary>Tile footprint in world units.</summary>
    const float TileWorldSize = 2f;

    /// <summary>Width of the safe edge platforms that ring the grid.</summary>
    const float RingWidth = 2f;

    /// <summary>Tile top surface. Must match the platform tops (scale 0.35 centred on y=0) so the
    /// grid and the ring stay flush.</summary>
    const float TileTopY = 0.175f;

    /// <summary>Player art is scaled to the CharacterController's height, so no fixed height here.</summary>

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

    /// <summary>
    /// A lit material, matching the crowd. The rest of this map is unlit, but the player is drawn
    /// with the same URP/Lit shading as the capsules so he reads as the same kind of object rather
    /// than a flat cutout sitting among them.
    /// </summary>
    static Material MatLit(string name, Color color)
    {
        string path = "Assets/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit")
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.color = color;
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent); go.transform.position = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat; return go;
    }

    /// <summary>The first Mesh sub-asset of a model file. AssetDatabase.LoadAssetAtPath&lt;Mesh&gt; does
    /// not work on model files, because the mesh is a sub-asset rather than the main object.</summary>
    static Mesh LoadMesh(string modelPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            if (o is Mesh m) return m;
        return null;
    }

    /// <summary>
    /// A tile is the artist's plate scaled to <see cref="TileWorldSize"/>, with a BoxCollider sized
    /// to the mesh's own local bounds so the collider hugs the plate.
    ///
    /// The collider has to be sized explicitly: the model is much thinner than it is wide, so a
    /// default unit BoxCollider on a uniformly scaled transform would be a cube, and bodies would
    /// float above the visible surface by the difference.
    /// </summary>
    static GameObject Tile(string name, Vector3 centre, Material mat, Mesh mesh, Transform parent)
    {
        if (mesh == null)
        {
            // Fall back to the old cube so a missing model degrades instead of breaking the map.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent); cube.transform.position = centre;
            cube.transform.localScale = new Vector3(TileWorldSize, 0.35f, TileWorldSize);
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            return cube;
        }

        Vector3 b = mesh.bounds.size;
        Vector3 c = mesh.bounds.center;
        float s = TileWorldSize / Mathf.Max(b.x, b.z);

        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.localScale = new Vector3(s, s, s);

        // World top of the plate = P + (c.y + b.y/2) * s, so solve that for the wanted top.
        float topOffset = TileTopY - (c.y + b.y * 0.5f) * s;
        go.transform.position = new Vector3(centre.x, topOffset, centre.z);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;

        var col = go.AddComponent<BoxCollider>();
        col.size = b;               // local units; the transform scale turns this into the 2x2 footprint
        col.center = c;

        return go;
    }

    /// <summary>The player is a bare body plus a visual child, because Bob's pivot is his centre:
    /// the visual has to be offset so his feet meet the floor, and that offset cannot live on the
    /// root without moving the CharacterController too.
    ///
    /// Note the offset is measured from the controller's UNDERSIDE, not from the transform. Unity's
    /// default CharacterController is 2 tall with its centre on the origin, so its underside sits a
    /// full unit below the transform - standing the model on the transform would leave it hanging
    /// in mid-air.</summary>
    static GameObject Player(Vector3 spawn, Material mat, Mesh mesh)
    {
        var player = new GameObject("Test Player");
        player.layer = GameBootstrap.PlayerLayer;
        player.transform.position = spawn;
        var cc = player.AddComponent<CharacterController>();
        player.AddComponent<TestPlayer>();

        // Lets the crowd shove the player without ever going through PhysX, which would resolve the
        // contact by moving the CharacterController - sometimes upward, which floats the player.
        var push = player.AddComponent<CrowdPushReceiver>();
        push.NpcMask = 1 << GameBootstrap.NpcLayer;

        // Punch, ported from the playerLogic branch: a hitbox parked ahead of the player and armed
        // for a short window by a press.
        var punchGo = new GameObject("PlayerPunch");
        punchGo.transform.SetParent(player.transform, false);
        var punchSphere = punchGo.AddComponent<SphereCollider>();
        punchSphere.isTrigger = true;
        punchSphere.radius = 0.9f;
        var punchHitbox = punchGo.AddComponent<PunchHitbox>();
        punchHitbox.TargetMask = 1 << GameBootstrap.NpcLayer;
        var playerPunch = player.AddComponent<PlayerPunch>();
        playerPunch.Hitbox = punchHitbox;
        playerPunch.TargetMask = 1 << GameBootstrap.NpcLayer;
        punchGo.SetActive(false);

        var visual = new GameObject("Visual");
        visual.layer = GameBootstrap.PlayerLayer;
        visual.transform.SetParent(player.transform, false);

        float ccBottom = cc.center.y - cc.height * 0.5f;

        if (mesh == null)
        {
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "Visual";
            fallback.transform.SetParent(player.transform, false);
            Object.DestroyImmediate(fallback.GetComponent<Collider>());
            float cs = cc.height * 0.5f;
            fallback.transform.localPosition = new Vector3(0f, ccBottom + cc.height * 0.5f, 0f);
            fallback.transform.localScale = new Vector3(cs, cs, cs);
            fallback.GetComponent<Renderer>().sharedMaterial = mat;
            return player;
        }

        Vector3 b = mesh.bounds.size;
        Vector3 c = mesh.bounds.center;
        float s = cc.height / Mathf.Max(0.0001f, b.y);

        // Bob is far wider for his height than a capsule is. Sizing the collider from height alone
        // leaves his body and arms outside it, and since everything that separates from the player
        // uses the collider radius, NPCs end up visibly overlapping him. Fit the collider to what is
        // actually drawn.
        cc.radius = Mathf.Max(cc.radius, b.x * s * 0.5f);

        visual.transform.localScale = new Vector3(s, s, s);
        // Solve localPosition.y + (c.y - b.y/2) * s = ccBottom so the lowest vertex meets the
        // controller's underside.
        visual.transform.localPosition = new Vector3(0f, ccBottom - (c.y - b.y * 0.5f) * s, 0f);

        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        visual.AddComponent<MeshRenderer>().sharedMaterial = mat;

        return player;
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
        var playerMat = MatLit("Player",Color.white);
        var tileMesh = LoadMesh(TileModelPath);
        var playerMesh = LoadMesh(PlayerModelPath);
        var root = new GameObject("Floating Map"); var map = root.AddComponent<FloatingMap>();
        var tiles = new List<Renderer>(); var platforms = new List<Renderer>();

        // Everything about the ring is derived from the grid, so changing GridSize cannot leave the
        // platforms overlapping the tiles or floating away from them.
        float halfGrid = GridSize * GridSpacing * 0.5f;          // grid edge
        float ringCentre = halfGrid + RingWidth * 0.5f;          // middle of the 2-wide ledge
        float ringSpan = halfGrid * 2f + RingWidth * 2f;         // full outer span
        float ringSideSpan = halfGrid * 2f;                      // west/east only bridge the middle
        var ringScaleLong = new Vector3(ringSpan, .35f, RingWidth);
        var ringScaleSide = new Vector3(RingWidth, .35f, ringSideSpan);

        float half = (GridSize - 1) * 0.5f;
        for (int z = 0; z < GridSize; z++) for (int x = 0; x < GridSize; x++)
        {
            var center = new Vector3((x-half)*GridSpacing, 0, (z-half)*GridSpacing);
            var tile = Tile("Tile " + (z*GridSize+x+1).ToString("00"),center,black,tileMesh,root.transform);
            tiles.Add(tile.GetComponent<Renderer>());
            for (int edge=0;edge<4;edge++)
            {
                bool alongX = edge < 2; float sign = edge % 2 == 0 ? -1 : 1;
                // Sit the line on the shared edge between neighbouring tiles. Nudged a hair inside
                // so the two coincident lines do not z-fight, and the line runs the full tile
                // length so corners meet instead of leaving notches.
                float off = TileWorldSize * 0.5f - 0.01f;
                var pos = center + new Vector3(alongX ? 0 : sign*off,.19f,alongX ? sign*off : 0);
                var line = Box("Outline",pos,alongX ? new Vector3(TileWorldSize,.025f,.035f) : new Vector3(.035f,.025f,TileWorldSize),outline,root.transform);
                Object.DestroyImmediate(line.GetComponent<Collider>());
            }
        }
        platforms.Add(Box("Start platform — south",new Vector3(0,0,-ringCentre),ringScaleLong,ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — north",new Vector3(0,0,ringCentre),ringScaleLong,ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — west",new Vector3(-ringCentre,0,0),ringScaleSide,ring,root.transform).GetComponent<Renderer>());
        platforms.Add(Box("Platform — east",new Vector3(ringCentre,0,0),ringScaleSide,ring,root.transform).GetComponent<Renderer>());
        var spawn = new GameObject("Spawn Point"); spawn.transform.position = new Vector3(0,1.3f,-ringCentre); spawn.transform.SetParent(root.transform);
        map.spawnPoint = spawn.transform; map.tiles = tiles.ToArray(); map.platforms = platforms.ToArray();
        var kill = Box("Kill Plane",new Vector3(0,-7,0),new Vector3(100,1,100),danger);
        kill.GetComponent<BoxCollider>().isTrigger = true; kill.AddComponent<KillPlane>();
        var body = kill.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        Player(spawn.transform.position, playerMat, playerMesh);

        // Everything the map itself uses is unlit, so it needs no light. The crowd spawned at
        // runtime uses the shared URP/Lit palette though, and a Lit surface with no light in the
        // scene renders black - so the scene needs one.
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
        RenderSettings.fog = false;

        // Bakes its own navmesh over this authored geometry and spawns agents as round
        // participants. Added here so a regenerated scene always has the crowd.
        root.AddComponent<FloatingTilesCrowd>();
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0,18,-20); camera.transform.LookAt(Vector3.zero);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.08f); camera.fieldOfView = 52;
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/FloatingTiles.unity");

        // Add rather than replace: assigning the array outright silently dropped every other scene
        // from the build settings.
        var sceneList = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!sceneList.Exists(s => s.path == "Assets/Scenes/FloatingTiles.unity"))
            sceneList.Add(new EditorBuildSettingsScene("Assets/Scenes/FloatingTiles.unity", true));
        EditorBuildSettings.scenes = sceneList.ToArray();

        AssetDatabase.SaveAssets();
        Debug.Log("FLOATING_MAP_BUILD_OK: 25 tiles, 4 platforms, kill plane, test player."
            + (tileMesh == null ? " WARNING: Tile_t2 mesh not found." : "")
            + (playerMesh == null ? " WARNING: Bob mesh not found." : ""));
    }
}
