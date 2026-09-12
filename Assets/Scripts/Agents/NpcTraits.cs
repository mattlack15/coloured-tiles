using UnityEngine;

namespace Jam
{
    public enum NpcArchetype
    {
        Cautious,
        Bold,
        Drifter,
        Anchor,
    }

    /// <summary>
    /// The whole personality surface of an NPC: a handful of numbers. Every archetype is just a
    /// preset over these, so there is exactly one scoring function to reason about.
    ///
    /// The default is to prefer tiles that are FAR away. A crowd that always takes the nearest
    /// match stands still; a crowd that takes the far match is constantly crossing the platform,
    /// which is what actually puts bodies in the player's way.
    /// </summary>
    [System.Serializable]
    public class NpcTraits
    {
        public NpcArchetype Archetype;

        /// <summary>How much the agent cares about the length of the trip.</summary>
        public float DistanceWeight = 1f;

        /// <summary>True = prefer the far lit tile, false = prefer the nearest one.</summary>
        public bool PrefersFar = true;

        /// <summary>How much the agent cares about a tile being contested. 0 = dives in blind.</summary>
        public float CongestionWeight = 1f;

        /// <summary>If > 0, the agent prefers targets at roughly this step distance (drifters).</summary>
        public float PreferredDistance = 0f;

        /// <summary>Seconds to think before reacting to a fresh beat. Staggered per agent.</summary>
        public float ReactionDelay = 0.35f;

        /// <summary>Seconds of no progress before the agent gives up on its target.</summary>
        public float Patience = 3f;

        /// <summary>How often a committed agent bothers to re-check its choice.</summary>
        public float ReconsiderInterval = 1.1f;

        /// <summary>How much better a rival tile has to score before the agent abandons a target
        /// it already committed to. This is what stops target dithering.</summary>
        public float SwitchMargin = 2.5f;

        public float SpeedFactor = 1f;

        public static NpcTraits Make(NpcArchetype archetype, float speedFactor, ref uint rng)
        {
            var t = new NpcTraits { Archetype = archetype, SpeedFactor = speedFactor };

            t.ReactionDelay = 0.18f + Palette.Hash01(ref rng) * 0.4f;
            t.ReconsiderInterval = 0.8f + Palette.Hash01(ref rng) * 0.7f;
            float reactionScale = 1f;

            switch (archetype)
            {
                case NpcArchetype.Bold:
                    // Ignores congestion entirely and takes the longest trip available: this is the
                    // one that body-blocks you.
                    t.CongestionWeight = 0.15f;
                    t.DistanceWeight = 1.1f;
                    t.PrefersFar = true;
                    t.Patience = 2.4f;
                    break;

                case NpcArchetype.Cautious:
                    // Wants the long trip but refuses to be squeezed, so it takes the road less
                    // travelled and accidentally shows the player a clean lane.
                    t.CongestionWeight = 2.1f;
                    t.DistanceWeight = 1f;
                    t.PrefersFar = true;
                    t.Patience = 3.5f;
                    break;

                case NpcArchetype.Drifter:
                    // Mid-range wanderer. Crosses the board without committing to either extreme.
                    t.CongestionWeight = 0.8f;
                    t.DistanceWeight = 0.7f;
                    t.PreferredDistance = 4f + Palette.Hash01(ref rng) * 4f;
                    t.Patience = 4.5f;
                    reactionScale = 1.6f;
                    break;

                case NpcArchetype.Anchor:
                    // The exception: always the nearest lit tile, and reacts fastest. It rarely has
                    // to cross the platform, so it claims reliably and reads as the competent one.
                    t.DistanceWeight = 1.45f;
                    t.PrefersFar = false;
                    t.CongestionWeight = 1.25f;
                    t.Patience = 3f;
                    reactionScale = 0.55f;
                    break;
            }

            t.ReactionDelay *= reactionScale;
            return t;
        }
    }
}
