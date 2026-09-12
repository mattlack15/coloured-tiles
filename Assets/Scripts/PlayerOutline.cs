using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Attach only to the locally controlled character, not the shared Bob model asset.
public class PlayerOutline : MonoBehaviour
{
    public Color outlineColour = Color.white;
    [Range(.005f, .15f)] public float outlineWidth = .045f;
    readonly List<Renderer> sources = new List<Renderer>();
    readonly List<Renderer> shells = new List<Renderer>();
    Material material;
    void Start()
    {
        var shader = Resources.Load<Shader>("PlayerOutline");
        if (!shader) { Debug.LogError("Player outline shader is missing.", this); return; }
        material = new Material(shader);
        foreach (var source in GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = source.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            var shell = new GameObject("Local Player Outline");
            shell.layer = source.gameObject.layer;
            shell.transform.SetParent(source.transform, false);
            shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = shell.AddComponent<MeshRenderer>();
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sources.Add(source); shells.Add(renderer);
        }
        UpdateOutline();
    }
    void LateUpdate() { UpdateOutline(); }
    void UpdateOutline()
    {
        if (!material) return;
        material.SetColor("_OutlineColor", outlineColour);
        material.SetFloat("_OutlineWidth", outlineWidth);
        for (int i = 0; i < shells.Count; i++)
            if (shells[i]) shells[i].enabled = isActiveAndEnabled && sources[i] && sources[i].enabled;
    }
    void OnDisable() { foreach (var shell in shells) if (shell) shell.enabled = false; }
    void OnDestroy()
    {
        foreach (var shell in shells) if (shell) Destroy(shell.gameObject);
        if (material) Destroy(material);
    }
}
