using System.Collections.Generic;

namespace Jam
{
    /// <summary>
    /// Everything the crowd needs to know about wherever it happens to be standing.
    ///
    /// The point of this interface is that the agents do not own the floor. They can run on the
    /// procedurally generated board in SampleScene or on the team's hand-built FloatingTiles
    /// arena without knowing which one they are on — each arena supplies the geometry, the colour
    /// state, and a signal for "a new assignment cycle has begun".
    /// </summary>
    public interface IArena
    {
        /// <summary>False until the arena has finished building itself; nothing may query it yet.</summary>
        bool Ready { get; }

        int Width { get; }
        int Height { get; }

        /// <summary>Lit tiles on the board right now, of any colour.</summary>
        int LitTileCount { get; }

        // ---- cells and world space ----

        bool InBounds(int x, int y);
        UnityEngine.Vector2Int WorldToCell(UnityEngine.Vector3 world);
        UnityEngine.Vector3 CellToWorld(UnityEngine.Vector2Int cell);

        /// <summary>A cell at least <paramref name="margin"/> cells in from the lip. Used for
        /// respawns, so it must never hand back a cell hanging over the void.</summary>
        UnityEngine.Vector2Int RandomInteriorCell(int margin);

        // ---- colour state ----

        bool IsLit(UnityEngine.Vector2Int cell);

        /// <summary>Only meaningful when <see cref="IsLit"/> is true.</summary>
        ColorId ColourOf(UnityEngine.Vector2Int cell);

        IReadOnlyList<UnityEngine.Vector2Int> TilesOfColor(ColorId c);

        // ---- the void ----

        UnityEngine.Vector3 PlatformCenter { get; }

        /// <summary>Horizontal distance to the lip. Negative once you are past it.</summary>
        float DistanceToEdge(UnityEngine.Vector3 world);

        // ---- pathing ----

        /// <summary>Single-source step counts over the walkable grid. <paramref name="dst"/> must be
        /// sized Width x Height and is filled with -1 for unreachable cells.</summary>
        void BfsFrom(UnityEngine.Vector2Int from, int[,] dst);

        /// <summary>
        /// Raised when the arena re-deals colours, which is the only moment an agent's colour
        /// changes. On the procedural board that is every colour flip; on the hand-built arena it
        /// is once per round. The argument is a monotonically increasing cycle counter.
        /// </summary>
        event System.Action<int> CycleAdvanced;
    }
}
