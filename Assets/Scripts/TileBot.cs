using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(TileActor))]
public class TileBot : MonoBehaviour
{
    public BotPreset preset;
    public BotPersonality personality;
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
    Vector3 jumpLanding;
    int passingSide;
    bool recovering;
    static readonly float[] Angles = { 0, 25, -25, 50, -50, 80, -80 };

    public void Initialize(BotPreset kind, System.Random random)
    {
        actor = GetComponent<TileActor>();
        preset = kind;
        personality = BotPersonality.Create(kind, random);
        passingSide = random.Next(2) == 0 ? -1 : 1;
        actor.PersonalityName = kind == BotPreset.CarefulPlanner ? "Planner" : kind.ToString();
    }
    public void ResetRound()
    {
        route.Clear();
        waypoint = 0;
        reactionUntil = float.PositiveInfinity;
        replanAt = blockedFor = recoveryUntil = pushUntil = jumpUntil = 0;
        recovering = false;
        State = "Waiting";
        actor.SetInput(Vector3.zero);
    }
    public void Reveal()
    {
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
        if (!actor.Map.Revealed || actor.Map.GameOver || !actor.Target || (actor.Map.Resolving && !actor.IsOnTarget()))
        {
            State = "Waiting"; actor.SetInput(Vector3.zero); return;
        }
        if (Time.time < reactionUntil) { State = "Reacting"; actor.SetInput(Vector3.zero); return; }
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
        if (Time.time >= replanAt && actor.Grounded) Plan(pushing);
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
        if (routeDistance < lastRouteDistance - .025f) { blockedFor = 0; recovering = false; }
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
    void MakeRoomAtDestination()
    {
        if (Flat(actor.TargetPosition - actor.Feet).sqrMagnitude > 6) return;
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
            Vector3 candidate = centre + new Vector3(x * .45f, 0, z * .45f);
            float cost = Flat(candidate - actor.Feet).magnitude;
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
