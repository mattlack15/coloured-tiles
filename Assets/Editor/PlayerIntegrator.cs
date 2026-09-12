using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerIntegrator
{
    static void WaitForStop()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= WaitForStop;
        Integrate();
    }
    [MenuItem("Floating Tiles/Use Player With Bob")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.update -= WaitForStop;
            EditorApplication.update += WaitForStop;
            return;
        }
        var map = Object.FindFirstObjectByType<FloatingMap>();
        var old = Object.FindFirstObjectByType<TestPlayer>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player.prefab");
        if (!map || !prefab) { Debug.LogError("Player integration needs FloatingMap and Assets/Player.prefab."); return; }
        GameObject root = null;
        foreach (var existing in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (existing != old) { root = existing.gameObject; break; }
        if (!root) root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Player";
        if (old) root.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
        // Keep the PlayerModel the user already positioned on Player.
        var oldAim = root.transform.Find("AimIndicator");
        if (oldAim) Object.DestroyImmediate(oldAim.gameObject);
        foreach (var collider in root.GetComponentsInChildren<Collider>())
            if (collider.gameObject != root) Object.DestroyImmediate(collider);
        foreach (var collider in root.GetComponents<Collider>()) Object.DestroyImmediate(collider);
        var body = root.GetComponent<Rigidbody>();
        if (body) { body.isKinematic = true; body.useGravity = false; }
        var controller = root.GetComponent<CharacterController>();
        if (!controller) controller = root.AddComponent<CharacterController>();
        if (old) EditorUtility.CopySerialized(old.GetComponent<CharacterController>(), controller);
        if (!root.transform.Find("PlayerModel"))
        {
            var bob = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bob (1).obj");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(bob, root.transform);
            visual.name = "PlayerModel";
        }
        var model = root.transform.Find("PlayerModel");
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            Vector3 scale = root.transform.lossyScale;
            controller.center = root.transform.InverseTransformPoint(bounds.center);
            controller.radius = Mathf.Max(bounds.extents.x / Mathf.Abs(scale.x), bounds.extents.z / Mathf.Abs(scale.z));
            controller.height = Mathf.Max(controller.radius * 2, bounds.size.y / Mathf.Abs(scale.y));
            controller.stepOffset = Mathf.Min(.3f, controller.height * .25f);
        }
        var player = root.GetComponent<Player>();
        if (old) { player.speed = old.speed; player.launchSpeed = old.launchSpeed; player.launchUpSpeed = old.launchUpSpeed; }
        if (!root.GetComponent<PlayerOutline>()) root.AddComponent<PlayerOutline>();
        map.player = player;
        if (old) Object.DestroyImmediate(old.gameObject);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, "Assets/Player.prefab", InteractionMode.AutomatedAction);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("Player prefab now has Bob, outline and arena controls; Test Player replaced.");
    }
}
