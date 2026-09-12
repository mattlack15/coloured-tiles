using UnityEngine;
public class KillPlane : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var actor = other.GetComponentInParent<TileActor>();
        if (actor) actor.Die();
    }
}
