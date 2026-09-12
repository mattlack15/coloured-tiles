using System.Collections.Generic;
using UnityEngine;

// A small terrain graph for this floating map. Characters never become hard obstacles.
public sealed class TileNavigation
{
    public struct Waypoint
    {
        public Vector3 position;
        public bool jump;
        public Waypoint(Vector3 position, bool jump = false) { this.position = position; this.jump = jump; }
    }
    struct Link
    {
        public int to;
        public float length;
        public bool jump;
    }
    sealed class Node
    {
        public Vector3 position;
        public float exposure;
        public readonly List<Link> links = new List<Link>();
    }
    const float Spacing = .55f;
    // Reach across the entire .2m seam even when the centre is close to one edge.
    const float SupportReach = TileActor.Radius * .6f;
    const int TerrainMask = Physics.DefaultRaycastLayers;
    readonly RaycastHit[] groundHits = new RaycastHit[32];
    readonly Collider[] bodyHits = new Collider[32];
    readonly List<Node> nodes = new List<Node>();
    readonly Dictionary<Vector2Int, int> cells = new Dictionary<Vector2Int, int>();
    readonly FloatingMap map;
    readonly float floorY;
    public int NodeCount => nodes.Count;
    public string LastFailure { get; private set; }

    public TileNavigation(FloatingMap map)
    {
        this.map = map;
        floorY = map.tiles[0].bounds.max.y;
        Build();
    }
    bool HasFloor(Vector3 p)
    {
        p.y = floorY + .35f;
        int count = Physics.RaycastNonAlloc(p, Vector3.down, groundHits, .5f, TerrainMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!groundHits[i].collider.GetComponentInParent<TileActor>()) return true;
        return false;
    }
    public bool Walkable(Vector3 p)
    {
        p.y = floorY;
        bool support = HasFloor(p) ||
            (HasFloor(p + Vector3.right * SupportReach) && HasFloor(p - Vector3.right * SupportReach)) ||
            (HasFloor(p + Vector3.forward * SupportReach) && HasFloor(p - Vector3.forward * SupportReach)) ||
            // Four tiles meet around a tiny cross-shaped gap. Opposite diagonal
            // contacts support the capsule even when all cardinal probes miss.
            (HasFloor(p + new Vector3(SupportReach, 0, SupportReach)) && HasFloor(p - new Vector3(SupportReach, 0, SupportReach))) ||
            (HasFloor(p + new Vector3(SupportReach, 0, -SupportReach)) && HasFloor(p - new Vector3(SupportReach, 0, -SupportReach)));
        return support && ClearBody(p);
    }
    bool ClearBody(Vector3 feet)
    {
        int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (TileActor.Radius + .035f),
            feet + Vector3.up * (TileActor.Height - TileActor.Radius), TileActor.Radius * .95f,
            bodyHits, TerrainMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (!bodyHits[i].GetComponentInParent<TileActor>()) return false;
        return count < bodyHits.Length;
    }
    public bool CanWalk(Vector3 a, Vector3 b)
    {
        a.y = b.y = floorY;
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .16f));
        for (int i = 0; i <= steps; i++) if (!Walkable(Vector3.Lerp(a, b, (float)i / steps))) return false;
        return true;
    }
    public float Exposure(Vector3 p)
    {
        int missing = 0;
        if (!HasFloor(p + Vector3.right * .4f)) missing++;
        if (!HasFloor(p - Vector3.right * .4f)) missing++;
        if (!HasFloor(p + Vector3.forward * .4f)) missing++;
        if (!HasFloor(p - Vector3.forward * .4f)) missing++;
        return missing * .25f;
    }
    bool CanJump(Vector3 a, Vector3 b)
    {
        if (!HasFloor(a) || !HasFloor(b)) return false;
        float duration = 2 * Mathf.Sqrt(2 * (map.Human ? map.Human.JumpHeightValue : TileActor.JumpHeight) / TileActor.Gravity);
        if (Vector3.Distance(a, b) > (map.Human ? map.Human.MoveSpeed : TileActor.Speed) * duration * .8f) return false;
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.up * (4 * (map.Human ? map.Human.JumpHeightValue : TileActor.JumpHeight) * t * (1 - t));
            if (!ClearBody(p)) return false;
        }
        return true;
    }
    void Build()
    {
        Physics.SyncTransforms();
        Bounds bounds = map.tiles[0].bounds;
        foreach (var tile in map.tiles) bounds.Encapsulate(tile.bounds);
        foreach (var platform in map.platforms) bounds.Encapsulate(platform.bounds);
        for (int z = Mathf.CeilToInt(bounds.min.z / Spacing); z <= Mathf.FloorToInt(bounds.max.z / Spacing); z++)
        for (int x = Mathf.CeilToInt(bounds.min.x / Spacing); x <= Mathf.FloorToInt(bounds.max.x / Spacing); x++)
        {
            Vector3 p = new Vector3(x * Spacing, floorY, z * Spacing);
            if (!Walkable(p)) continue;
            cells.Add(new Vector2Int(x, z), nodes.Count);
            nodes.Add(new Node { position = p, exposure = Exposure(p) });
        }
        foreach (var cell in cells)
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            var direction = new Vector2Int(dx, dz);
            Node from = nodes[cell.Value];
            if (cells.TryGetValue(cell.Key + direction, out int next) && CanWalk(from.position, nodes[next].position))
            {
                from.links.Add(new Link { to = next, length = Vector3.Distance(from.position, nodes[next].position) });
                continue;
            }

        }
    }
    int Nearest(Vector3 p)
    {
        p.y = floorY;
        int result = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < nodes.Count; i++)
        {
            float distance = (nodes[i].position - p).sqrMagnitude;
            if (distance >= best || !CanWalk(p, nodes[i].position)) continue;
            best = distance;
            result = i;
        }
        return result;
    }
    public bool FindPath(TileActor actor, Vector3 destination, BotPersonality personality, bool urgent, List<Waypoint> output)
    {
        output.Clear();
        LastFailure = null;
        int start = Nearest(actor.Feet), end = Nearest(destination);
        if (start < 0 || end < 0)
        {
            LastFailure = $"start={start} end={end} feet={actor.Feet} destination={destination} walkable={Walkable(actor.Feet)}";
            return false;
        }
        int count = nodes.Count;
        var cost = new float[count];
        var crowd = new float[count];
        var parent = new int[count];
        var jump = new bool[count];
        var closed = new bool[count];
        var open = new List<int> { start };
        float crowdWeight = urgent ? .02f : Mathf.Lerp(2.4f, .2f, personality.assertiveness);
        for (int i = 0; i < count; i++)
        {
            cost[i] = float.PositiveInfinity;
            parent[i] = -1;
            foreach (var other in map.Actors)
            {
                if (other == actor || !other.isActiveAndEnabled || other.IsDead) continue;
                Vector3 predicted = other.transform.position + other.Velocity * .25f;
                predicted.y = floorY;
                crowd[i] += Mathf.Max(0, 1 - Vector3.Distance(predicted, nodes[i].position) / 1.25f) * crowdWeight;
            }
        }
        cost[start] = 0;
        while (open.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < open.Count; i++)
                if (cost[open[i]] + Vector3.Distance(nodes[open[i]].position, nodes[end].position)
                    < cost[open[best]] + Vector3.Distance(nodes[open[best]].position, nodes[end].position)) best = i;
            int current = open[best];
            open.RemoveAt(best);
            if (current == end)
            {
                while (current != start)
                {
                    output.Add(new Waypoint(nodes[current].position, jump[current]));
                    current = parent[current];
                }
                output.Add(new Waypoint(nodes[start].position));
                output.Reverse();
                destination.y = floorY;
                output.Add(new Waypoint(destination));
                return true;
            }
            closed[current] = true;
            foreach (var link in nodes[current].links)
            {
                if (closed[link.to]) continue;
                float risk = nodes[link.to].exposure * (1 - personality.riskTolerance) * .6f;
                float candidate = cost[current] + link.length * (1 + crowd[link.to] + risk)
                    + (link.jump ? Mathf.Lerp(3, .5f, personality.riskTolerance) : 0);
                if (candidate >= cost[link.to]) continue;
                if (float.IsPositiveInfinity(cost[link.to])) open.Add(link.to);
                cost[link.to] = candidate;
                parent[link.to] = current;
                jump[link.to] = link.jump;
            }
        }
        LastFailure = $"Disconnected terrain: start={start} end={end}";
        return false;
    }
}
