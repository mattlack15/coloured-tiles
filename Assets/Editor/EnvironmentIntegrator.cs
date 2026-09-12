using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnvironmentIntegrator
{
    [MenuItem("Floating Tiles/Use ENV Export")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.update -= Wait;
            EditorApplication.update += Wait;
            return;
        }
        EditorSceneManager.OpenScene("Assets/Scenes/FloatingTiles.unity");
        var map = Object.FindFirstObjectByType<FloatingMap>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/the ENV.obj");
        if (!map || !source || map.transform.Find("Environment")) return;
        var oldFloor = map.transform.Find("Imported Floor");
        var oldWalls = map.transform.Find("Imported Walls");
        if (!oldFloor) return;
        var env = new GameObject("Environment");
        env.transform.SetParent(map.transform, false);
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/EnvironmentMeshes" }))
        {
            var piece = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var part = (GameObject)PrefabUtility.InstantiatePrefab(piece, env.transform);
            part.name = piece.name;
        }
        env.name = "Environment";
        env.transform.localPosition = oldFloor.localPosition;
        env.transform.localRotation = oldFloor.localRotation;
        env.transform.localScale = oldFloor.localScale;
        Renderer floor = null;
        foreach (var renderer in env.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.transform.parent.name.Contains("BFloor")) floor = renderer;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var original = materials[i];
                string path = "Assets/Generated/ENV_" + renderer.transform.parent.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(Shader.Find("FloatingTiles/ArenaStone"));
                    material.color = original.HasProperty("_Color") ? original.color : Color.gray;
                    AssetDatabase.CreateAsset(material, path);
                }
            if (renderer.transform.parent.name == "M_Corner_03") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Corner_03.001") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Corner_03.002") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Corner_03.003") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Brickwall_T2") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Brickwall_T1") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Brickwall_T3") material.color = new Color(0.477293f,0.472973f,0.429985f);
            if (renderer.transform.parent.name == "M_Brickwall_T4") material.color = new Color(0.477293f,0.472973f,0.429985f);
            if (renderer.transform.parent.name == "M_BFloor") material.color = new Color(0.263932f,0.263932f,0.263932f);
            if (renderer.transform.parent.name == "M_wall_02") material.color = new Color(0.263932f,0.263932f,0.263932f);
            if (renderer.transform.parent.name == "M_wall_01") material.color = new Color(0.263932f,0.263932f,0.263932f);
            if (renderer.transform.parent.name == "M_wall_03") material.color = new Color(0.263932f,0.263932f,0.263932f);
            if (renderer.transform.parent.name == "M_wall_04") material.color = new Color(0.263932f,0.263932f,0.263932f);
            if (renderer.transform.parent.name == "M_Wall_Foot") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Wall_Top") material.color = new Color(0.258744f,0.256326f,0.214500f);
            if (renderer.transform.parent.name == "M_Banner") material.color = new Color(0.504131f,0.497557f,0.153400f);
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        }
        if (!floor) { Object.DestroyImmediate(env); Debug.LogError("ENV floor mesh not found; existing environment retained."); return; }
        PrefabUtility.RecordPrefabInstancePropertyModifications(env.transform);
        map.platforms = new[] { floor };
        Object.DestroyImmediate(oldFloor.gameObject);
        if (oldWalls) Object.DestroyImmediate(oldWalls.gameObject);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("ENV export integrated with source material colours and floor round logic.");
    }
    static void Wait()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= Wait;
        Integrate();
    }
}
