using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WallIntegrator
{
    [MenuItem("Floating Tiles/Integrate Imported Walls")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = Object.FindFirstObjectByType<FloatingMap>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Walls.obj");
        if (!map || !source || map.transform.Find("Imported Walls")) return;
        var floor = map.transform.Find("Imported Floor");
        if (!floor) return;
        var walls = (GameObject)PrefabUtility.InstantiatePrefab(source, map.transform);
        Undo.RegisterCreatedObjectUndo(walls, "Integrate imported walls");
        walls.name = "Imported Walls";
        // Both exports use the same Blender origin and units.
        walls.transform.localPosition = floor.localPosition;
        walls.transform.localRotation = floor.localRotation;
        walls.transform.localScale = floor.localScale;
        const string materialPath = "Assets/Generated/ArenaStone.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(Shader.Find("FloatingTiles/ArenaStone"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        foreach (var renderer in walls.GetComponentsInChildren<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            var collider = renderer.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(walls.transform);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = walls;
        Debug.Log("Imported walls aligned with floor and mesh colliders added.");
    }
}
