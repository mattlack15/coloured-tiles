using UnityEngine;

/// <summary>
/// Anything that touches the kill plane loses a life. Routing this through TileParticipant means
/// the crowd is handled by the same rule as the player, instead of only the solo TestPlayer.
/// </summary>
public class KillPlane : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var participant = other.GetComponentInParent<Jam.TileParticipant>();
        if (participant != null)
        {
            participant.LoseLife();
            return;
        }

        var player = other.GetComponentInParent<TestPlayer>();
        if (player) player.Die();
    }
}
