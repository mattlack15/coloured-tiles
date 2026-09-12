using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(TileActor))]
public class TileBot : MonoBehaviour
{
    public BotPreset preset;
    public BotPersonality personality;
    [Tooltip("Prefer a spot near the tile centre when other occupants leave enough room.")]
    public bool preferTileCentre;
    public string State { get; private set; } = "Waiting";
    public int Replans { get; private set; }
    public int AvoidanceDecisions { get; private set; }
    public int PushDecisions { get; private set; }
    public IReadOnlyList<TileNavigation.Waypoint> Route => route;
    readonly List<TileNavigation.Waypoint> route = new List<TileNavigation.Waypoint>();
    TileActor actor;
    int waypoint;
    float reactionUntil, replanAt, blockedFor, recoveryUntil, pushUntil, lastRouteDistance;
    float jumpUntil;
    float progressSinceCheck;
    Vector3 jumpLanding;
    Vector3 preferredStandingSpot;
    float reconsiderStandingSpotAt;
    int passingSide;
    bool recovering;
    bool reportedNoRoute;
    bool reachedTarget;
    float braceUntil;
    static readonly float[] Angles = { 0, 25, -25, 50, -50, 80, -80 };

    public void Initialize(BotPreset kind, System.Random random)
    {
        actor = GetComponent<TileActor>();
        preset = kind;
        personality = BotPersonality.Create(kind, random);
        preferTileCentre = kind == BotPreset.CarefulPlanner;
        passingSide = random.Next(2) == 0 ? -1 : 1;
        actor.PersonalityName = kind == BotPreset.CarefulPlanner ? "Planner" : kind.ToString();
    }
    public void ResetRound()
    {
        route.Clear();
        waypoint = 0;
        reactionUntil = float.PositiveInfinity;
        replanAt = blockedFor = recoveryUntil = pushUntil = jumpUntil = 0;
        progressSinceCheck = 0;
        reconsiderStandingSpotAt = 0;
        recovering = false;
        reportedNoRoute = false;
        reachedTarget = false;
        braceUntil = 0;
        State = "Waiting";
        actor.SetInput(Vector3.zero);
    }
    public void Reveal()
    {
        if (actor.Target)
        {
            Vector3 centre = actor.Target.bounds.center;
            centre.y = actor.Target.bounds.max.y;
            // Keep a slight offset toward the assigned corner, rather than making
            // every centre-preferring bot aim at precisely the same point.
            preferredStandingSpot = Vector3.Lerp(centre, actor.TargetPosition, .4f);
        }
        reactionUntil = Time.time + Mathf.Max(.05f, personality.reactionDelay + Random.Range(-.05f, .05f));
        State = "Reacting";
    }
    float RouteDistance()
    {
        Vector3 previous = actor.Feet;
        float distance = 0;
        for (int i = waypoint; i < route.Count; i++)
        {
            distance += Vector3.Distance(Flat(previous), Flat(route[i].position));
            previous = route[i].position;
        }
        return distance;
    }
    static Vector3 Flat(Vector3 p) { p.y = 0; return p; }
    void Plan(bool urgent)
    {
        actor.Map.Navigation.FindPath(actor, actor.TargetPosition, personality, urgent, route);
        if (route.Count == 0 && !reportedNoRoute)
        {
            Debug.Log($"BOT_ROUTE_DIAGNOSTIC {name}: {actor.Map.Navigation.LastFailure} grounded={actor.Grounded}");
            reportedNoRoute = true;
        }
        waypoint = 0;
        Replans++;
        if (!urgent && RouteDistance() / actor.MoveSpeed + .4f > actor.Map.Remaining)
        {
            actor.Map.Navigation.FindPath(actor, actor.TargetPosition, personality, true, route);
            pushUntil = Time.time + .5f;
        }
        lastRouteDistance = RouteDistance();
        replanAt = Time.time + Mathf.Lerp(.35f, 1.2f, personality.commitment);
    }
    public void Tick(float dt)
    {
        if (!actor || !actor.Map) return;
        if (actor.IsLaunched) { State = "Launched"; actor.SetInput(Vector3.zero); return; }
        if (actor.IsDead) { State = "Eliminated"; actor.SetInput(Vector3.zero); return; }
        if (!actor.Map.Revealed || actor.Map.GameOver || !actor.Target
            || (actor.Map.Resolving && !reachedTarget && !actor.IsOnTarget()))
        {
            State = "Waiting"; actor.SetInput(Vector3.zero); return;
        }
        if (Time.time < reactionUntil) { State = "Reacting"; actor.SetInput(Vector3.zero); return; }
        if (actor.Grounded && actor.IsOnTarget()) reachedTarget = true;
        // Defend before the ordinary "Holding" state, which otherwise stops all input.
        if (DefendTile()) return;
        var nav = actor.Map.Navigation;
        Vector3 position = actor.Feet;
        MakeRoomAtDestination();
        Vector3 targetDelta = Flat(actor.TargetPosition - position);
        if (Time.time < jumpUntil)
        {
            float remaining = Mathf.Max(.08f, jumpUntil - Time.time);
            actor.SetInput(Flat(jumpLanding - position) / (remaining * actor.MoveSpeed));
            State = "Jumping";
            return;
        }
        // A reserved anchor leaves room for other arrivals; occupants remain physical obstacles.
        if (actor.IsOnTarget() && targetDelta.magnitude < .15f)
        {
            State = "Holding";
            blockedFor = 0;
            actor.SetInput(Vector3.zero);
            return;
        }
        bool urgent = targetDelta.magnitude / actor.MoveSpeed + .8f >= actor.Map.Remaining;
        bool pushing = urgent || Time.time < pushUntil;
        if (Time.time >= replanAt && (actor.Grounded || nav.Walkable(position))) Plan(pushing);
        if (route.Count == 0)
        {
            State = "No route";
            actor.SetInput(Vector3.zero);
            return;
        }
        while (waypoint < route.Count - 1 && !route[waypoint].jump
            && Flat(route[waypoint].position - position).magnitude < .22f) waypoint++;
        // Look ahead along walkable segments so the grid does not cause stop-start motion.
        for (int i = waypoint + 1; i < Mathf.Min(waypoint + 4, route.Count); i++)
        {
            if (route[i].jump || route[waypoint].jump || !nav.CanWalk(position, route[i].position)) break;
            waypoint = i;
        }
        if (route[waypoint].jump && actor.Grounded)
        {
            jumpLanding = route[waypoint].position;
            float duration = 2 * Mathf.Sqrt(2 * actor.JumpHeightValue / TileActor.Gravity);
            jumpUntil = Time.time + duration;
            actor.SetInput(Flat(jumpLanding - position) / (duration * actor.MoveSpeed), true);
            waypoint = Mathf.Min(waypoint + 1, route.Count - 1);
            return;
        }
        Vector3 desired = Flat(route[waypoint].position - position).normalized;
        float routeDistance = RouteDistance();
        // Accumulate distance across frames: a minimum per-frame movement falsely
        // reports a stall at high frame rates, even at full movement speed.
        progressSinceCheck += lastRouteDistance - routeDistance;
        if (progressSinceCheck >= .05f)
        {
            blockedFor = 0;
            recovering = false;
            progressSinceCheck = 0;
        }
        else blockedFor += dt;
        lastRouteDistance = routeDistance;
        float patienceSeconds = Mathf.Lerp(.35f, 1.1f, personality.patience);
        if (blockedFor > patienceSeconds && !recovering)
        {
            recovering = true;
            recoveryUntil = Time.time + .22f;
            replanAt = recoveryUntil;
        }
        if (Time.time < recoveryUntil && !urgent)
        {
            Vector3 retreat = -desired * .35f + Vector3.Cross(Vector3.up, desired) * passingSide * .65f;
            actor.SetInput(nav.CanWalk(position, position + retreat * .4f) ? retreat : Vector3.zero);
            State = "Recovering";
            return;
        }
        if (recovering || (personality.assertiveness > .7f && blockedFor > .15f))
        {
            pushing = true;
            pushUntil = Time.time + .4f;
        }
        float approach = Mathf.Clamp01(targetDelta.magnitude / .65f);
        Vector3 selected = ChooseVelocity(desired * approach, pushing);
        State = pushing ? "Pushing" : Vector3.Dot(selected.normalized, desired) < .94f ? "Avoiding" : "Moving";
        if (State == "Avoiding") AvoidanceDecisions++;
        if (State == "Pushing") PushDecisions++;
        actor.SetInput(selected);
    }
    bool DefendTile()
    {
        if (!reachedTarget || !actor.Target.gameObject.activeSelf || actor.MoveSpeed <= 0) return false;
        Bounds bounds = actor.Target.bounds;
        Vector3 feet = actor.Feet;
        // A body still supported at the lip can step back; a falling body gets no rescue force.
        if (feet.y < bounds.max.y - .2f || feet.y > bounds.max.y + .3f
            || feet.x < bounds.min.x - .2f || feet.x > bounds.max.x + .2f
            || feet.z < bounds.min.z - .2f || feet.z > bounds.max.z + .2f) return false;

        float margin = Mathf.Lerp(.42f, .27f, personality.riskTolerance);
        Vector3 push = Flat(actor.PushVelocity);
        Vector3 predicted = feet + Flat(actor.Velocity) * .2f;
        bool approachingEdge = predicted.x < bounds.min.x + margin || predicted.x > bounds.max.x - margin
            || predicted.z < bounds.min.z + margin || predicted.z > bounds.max.z - margin;
        if (push.sqrMagnitude > .04f || approachingEdge) braceUntil = Time.time + .25f;
        else if (Time.time >= braceUntil) return false;

        Vector3 safe = feet;
        safe.x = Mathf.Clamp(feet.x, bounds.min.x + margin + .08f, bounds.max.x - margin - .08f);
        safe.z = Mathf.Clamp(feet.z, bounds.min.z + margin + .08f, bounds.max.z - margin - .08f);
        Vector3 correction = Flat(safe - feet) * 8 - push * Mathf.Lerp(.85f, 1f, personality.assertiveness);
        Vector3 sideways = Vector3.Cross(Vector3.up, Flat(bounds.center - feet).normalized);
        Vector3 chosen = Vector3.ClampMagnitude(correction, actor.MoveSpeed);
        float best = float.PositiveInfinity;
        for (int side = -1; side <= 1; side++)
        {
            Vector3 candidate = Vector3.ClampMagnitude(correction + sideways * side * actor.MoveSpeed * .35f, actor.MoveSpeed);
            Vector3 next = feet + (candidate + push) * .18f;
            if (next.x < bounds.min.x + .12f || next.x > bounds.max.x - .12f
                || next.z < bounds.min.z + .12f || next.z > bounds.max.z - .12f) continue;
            float cost = (candidate - correction).sqrMagnitude * .04f;
            foreach (var other in actor.Map.Actors)
            {
                if (other == actor || !other.isActiveAndEnabled || other.IsDead || other.IsLaunched) continue;
                float overlap = Mathf.Max(0, actor.BodyRadius + other.BodyRadius
                    - Flat(next - other.Feet - other.Velocity * .18f).magnitude);
                cost += overlap * overlap * 5;
            }
            if (cost < best) { best = cost; chosen = candidate; }
        }
        actor.SetInput(chosen / actor.MoveSpeed);
        State = "Bracing";
        blockedFor = 0;
        replanAt = 0;
        return true;
    }
    void MakeRoomAtDestination()
    {
        if (Flat(actor.TargetPosition - actor.Feet).sqrMagnitude > 6) return;
        if (preferTileCentre && Time.time >= reconsiderStandingSpotAt)
        {
            reconsiderStandingSpotAt = Time.time + .6f;
            if (Flat(preferredStandingSpot - actor.TargetPosition).sqrMagnitude > .01f
                && StandingSpotAvailable(preferredStandingSpot))
            {
                actor.SetStandingPosition(preferredStandingSpot);
                replanAt = 0;
            }
        }
        bool occupied = false;
        foreach (var other in actor.Map.Actors)
        {
            if (other == actor || !other.isActiveAndEnabled || other.IsDead) continue;
            if (Flat(other.Feet - actor.TargetPosition).magnitude < TileActor.Radius * 2 + .03f
                && Flat(other.Velocity).magnitude < .4f) { occupied = true; break; }
        }
        if (!occupied) return;
        Vector3 centre = actor.Target.bounds.center;
        centre.y = actor.Target.bounds.max.y;
        Vector3 chosen = actor.TargetPosition;
        float best = float.PositiveInfinity;
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            Vector3 candidate = centre + new Vector3(x * .54f, 0, z * .54f);
            float cost = Flat(candidate - actor.Feet).magnitude;
            if (preferTileCentre) cost += Flat(candidate - preferredStandingSpot).magnitude * 1.5f;
            bool clear = true;
            foreach (var other in actor.Map.Actors)
            {
                if (other == actor || !other.isActiveAndEnabled || other.IsDead) continue;
                if (Flat(other.Feet - candidate).magnitude < TileActor.Radius * 2 + .04f) { clear = false; break; }
                if (other.Target == actor.Target && Flat(other.TargetPosition - candidate).magnitude < TileActor.Radius * 2)
                    cost += 2;
            }
            if (clear && cost < best) { chosen = candidate; best = cost; }
        }
        if (!float.IsPositiveInfinity(best) && Flat(chosen - actor.TargetPosition).sqrMagnitude > .01f)
        {
            actor.SetStandingPosition(chosen);
            replanAt = 0;
        }
    }
    bool StandingSpotAvailable(Vector3 candidate)
    {
        foreach (var other in actor.Map.Actors)
        {
            if (other == actor || !other.isActiveAndEnabled || other.IsDead || other.IsLaunched) continue;
            float spacing = actor.BodyRadius + other.BodyRadius + .03f;
            if (Flat(other.Feet - candidate).magnitude < spacing) return false;
            if (other.Target == actor.Target && Flat(other.TargetPosition - candidate).magnitude < spacing) return false;
        }
        return true;
    }
    Vector3 ChooseVelocity(Vector3 desired, bool pushing)
    {
        var nav = actor.Map.Navigation;
        Vector3 position = actor.Feet;
        Vector3 best = Vector3.zero;
        float bestCost = float.PositiveInfinity;
        foreach (float angle in Angles)
        {
            Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
            Vector3 future = position + candidate * actor.MoveSpeed * .18f;
            if (!nav.CanWalk(position, future)) continue;
            float cost = (candidate - desired).sqrMagnitude * 1.8f;
            cost += nav.Exposure(future) * (1 - personality.riskTolerance) * .3f;
            if (angle * passingSide < 0) cost += .08f;
            foreach (var other in actor.Map.Actors)
            {
                if (other == actor || !other.isActiveAndEnabled || other.IsDead || Mathf.Abs(other.Feet.y - position.y) > TileActor.Height) continue;
                Vector3 offset = Flat(other.Feet - position);
                if (offset.sqrMagnitude > 9) continue;
                Vector3 relative = Flat(other.Velocity) - candidate * actor.MoveSpeed;
                float closestTime = Mathf.Clamp(-Vector3.Dot(offset, relative) / Mathf.Max(.01f, relative.sqrMagnitude), 0, .4f);
                float clearance = (offset + relative * closestTime).magnitude;
                float personalSpace = TileActor.Radius * 2 + Mathf.Lerp(.42f, .06f, personality.assertiveness);
                float collision = Mathf.Max(0, personalSpace - clearance) / personalSpace;
                cost += collision * collision * (pushing ? .08f : Mathf.Lerp(8, 2.5f, personality.assertiveness));
            }
            if (cost < bestCost) { bestCost = cost; best = candidate; }
        }
        // A short yield is allowed; blocked progress eventually triggers recovery/pushing.
        if (!pushing && bestCost > 2 && blockedFor < Mathf.Lerp(.15f, .6f, personality.patience)) return best * .2f;
        return best;
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 previous = transform.position;
        for (int i = waypoint; i < route.Count; i++)
        {
            Vector3 next = route[i].position + Vector3.up * .1f;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
