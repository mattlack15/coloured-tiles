using UnityEngine;
public class KillPlane : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<TestPlayer>();
        if (player) player.Die();
    }
}
