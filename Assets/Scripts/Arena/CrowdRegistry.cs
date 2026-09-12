using System.Collections.Generic;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Crowd-owned bookkeeping: which tile each agent is standing on, and which tile each agent has
    /// committed to walking to.
    ///
    /// This deliberately lives outside the arena. It is not a property of the floor — it is a
    /// property of the crowd standing on it — and keeping it here is what lets a single arena
    /// implementation serve both scenes without carrying agent state it does not care about.
    ///
    /// Dictionary-backed rather than a flat array: the two arenas have different shapes, one of
    /// them is only 25 cells, and the counts are trivially small at this crowd size.
    /// </summary>
    public class CrowdRegistry
    {
        readonly Dictionary<Vector2Int, int> _occupancy = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, int> _inbound = new Dictionary<Vector2Int, int>();

        /// <summary>Agents physically standing on this cell.</summary>
        public int Occupancy(Vector2Int cell) => _occupancy.TryGetValue(cell, out int n) ? n : 0;

        /// <summary>Agents that have committed to this cell as their target.</summary>
        public int Inbound(Vector2Int cell) => _inbound.TryGetValue(cell, out int n) ? n : 0;

        public void AddOccupant(Vector2Int cell) => Bump(_occupancy, cell, 1);
        public void RemoveOccupant(Vector2Int cell) => Bump(_occupancy, cell, -1);
        public void AddInbound(Vector2Int cell) => Bump(_inbound, cell, 1);
        public void RemoveInbound(Vector2Int cell) => Bump(_inbound, cell, -1);

        /// <summary>Total bodies within a square radius. Cheap approximation of "is this spot busy".</summary>
        public int OccupancyAround(Vector2Int cell, int radius)
        {
            int total = 0;
            for (int x = cell.x - radius; x <= cell.x + radius; x++)
                for (int y = cell.y - radius; y <= cell.y + radius; y++)
                    total += Occupancy(new Vector2Int(x, y));
            return total;
        }

        public void Clear()
        {
            _occupancy.Clear();
            _inbound.Clear();
        }

        static void Bump(Dictionary<Vector2Int, int> map, Vector2Int cell, int delta)
        {
            map.TryGetValue(cell, out int current);
            int next = Mathf.Max(0, current + delta);

            // Drop empty entries so the map does not grow one key per cell ever visited.
            if (next == 0) map.Remove(cell);
            else map[cell] = next;
        }
    }
}
