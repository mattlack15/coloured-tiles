using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FloorIntegrator
{
    [MenuItem("Floating Tiles/Integrate Imported Floor")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before integrating the floor.");
            return;
        }
        var map = Object.FindFirstObjectByType<FloatingMap>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floor.obj");
        if (!map || !source || map.platforms.Length == 0) return;
        if (map.platforms.Length == 1 && map.platforms[0].transform.IsChildOf(map.transform)
            && map.platforms[0].GetComponent<MeshCollider>()) return;

        var oldPlatforms = map.platforms;
        float top = oldPlatforms[0].bounds.max.y;
        var material = oldPlatforms[0].sharedMaterial;
        var floor = (GameObject)PrefabUtility.InstantiatePrefab(source, map.transform);
        Undo.RegisterCreatedObjectUndo(floor, "Integrate imported floor");
        floor.name = "Imported Floor";
        // The source opening is +/-2.920029; the existing tile field is +/-5.4.
        float scale = 5.4f / 2.920029f;
        floor.transform.localScale = Vector3.one * scale;
        floor.transform.position = map.transform.position;
        var renderer = floor.GetComponentInChildren<MeshRenderer>();
        floor.transform.position += Vector3.up * (top - renderer.bounds.max.y);
        renderer.sharedMaterial = material;
        var collider = renderer.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Undo.RecordObject(map, "Connect imported floor to rounds");
        map.platforms = new Renderer[] { renderer };
        foreach (var platform in oldPlatforms) Undo.DestroyObjectImmediate(platform.gameObject);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        Selection.activeGameObject = floor;
        Debug.Log("Imported floor integrated with round visibility and mesh collision.");
    }
}
