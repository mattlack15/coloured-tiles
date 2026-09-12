namespace Jam
{
    /// <summary>
    /// Everything the crowd needs from whatever is running the round.
    ///
    /// Paired with <see cref="IArena"/>, this is the second half of making the crowd portable. The
    /// arena says where the floor and the colours are; this says what the rules are right now and
    /// who to tell when an agent scores or dies. Splitting them means one crowd implementation can
    /// be driven by the procedural SampleScene board or by the hand-built FloatingTiles round loop
    /// without either of them knowing about the other.
    /// </summary>
    public interface ICrowdContext
    {
        /// <summary>False during build-up and after the game is over. Agents stand down.</summary>
        bool RoundActive { get; }

        /// <summary>How far in from the lip a fallen agent is put back.</summary>
        int NpcRespawnMargin { get; }

        /// <summary>Minimum seconds between two claims by the same agent.</summary>
        float ClaimCooldown { get; }

        /// <summary>Debug overlay: draw each agent's committed target.</summary>
        bool DrawTargetLines { get; }

        // ---- being shoved off the edge ----
        // Agents clamp themselves to the navmesh, so they can never walk off on their own; they
        // have to be squeezed off. These tune when a squeeze counts as fatal.

        float SlipEdgeMargin { get; }
        float SlipRadius { get; }
        int SlipCrowd { get; }
        float SlipOutwardDot { get; }
        float SlipTime { get; }
        float SlipImpulse { get; }
        float SlipHop { get; }

        void OnNpcClaim(NpcBrain brain);
        void OnNpcFall(NpcBrain brain);
    }
}
