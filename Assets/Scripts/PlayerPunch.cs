using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TestPlayer))]
public class PlayerPunch : MonoBehaviour
{
    public float reach = 1.7f;
    public float radius = .65f;
    public float cooldown = .55f;
    TestPlayer player;
    PunchHitbox hitbox;
    LineRenderer aim;
    Material material;
    Vector3 direction = Vector3.forward, strikeDirection;
    float readyAt, activeUntil;
    readonly HashSet<TileActor> hit = new HashSet<TileActor>();

    void Start()
    {
        player = GetComponent<TestPlayer>();
        hitbox = gameObject.AddComponent<PunchHitbox>();
        var indicator = new GameObject("Punch Aim");
        indicator.transform.SetParent(transform, false);
        aim = indicator.AddComponent<LineRenderer>();
        material = new Material(Shader.Find("Unlit/Color"));
        material.color = Color.white;
        aim.sharedMaterial = material;
        aim.positionCount = 2;
        aim.startWidth = .07f;
        aim.endWidth = .02f;
        aim.useWorldSpace = true;
    }
    void Update()
    {
        var actor = player.Actor;
        bool enabled = player.ControlsEnabled && actor && !actor.IsDead && !actor.IsLaunched
            && actor.Map && !actor.Map.GameOver;
        aim.enabled = enabled;
        if (!enabled) { activeUntil = 0; return; }
        Vector3 origin = actor.Feet + Vector3.up * .65f;
        var camera = Camera.main;
        if (camera && Mouse.current != null)
        {
            Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (new Plane(Vector3.up, origin).Raycast(ray, out float distance))
            {
                Vector3 delta = ray.GetPoint(distance) - origin;
                delta.y = 0;
                if (delta.sqrMagnitude > .01f) direction = delta.normalized;
            }
        }
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.sqrMagnitude > .04f) direction = new Vector3(stick.x, 0, stick.y).normalized;
        }
        bool pressed = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            || (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
        if (pressed && Time.time >= readyAt)
        {
            readyAt = Time.time + cooldown;
            activeUntil = Time.time + .2f;
            strikeDirection = direction;
            hit.Clear();
        }
        bool striking = Time.time < activeUntil;
        Vector3 forward = striking ? strikeDirection : direction;
        Vector3 end = origin + forward * reach;
        aim.SetPosition(0, origin);
        aim.SetPosition(1, end);
        aim.startWidth = striking ? .3f : .07f;
        material.color = striking ? Color.yellow : Time.time < readyAt ? Color.gray : Color.white;
        if (!striking) return;
        foreach (var target in actor.Map.Actors)
        {
            if (!target || target == actor || target.IsDead || target.IsLaunched || hit.Contains(target)) continue;
            Vector3 offset = target.Feet - actor.Feet;
            if (Mathf.Abs(offset.y) > 1.2f) continue;
            offset.y = 0;
            float along = Vector3.Dot(offset, forward);
            if (along < 0 || along > reach + target.BodyRadius) continue;
            Vector3 nearest = forward * Mathf.Clamp(along, 0, reach);
            if ((offset - nearest).magnitude > radius + target.BodyRadius) continue;
            bool blocked = false;
            Vector3 targetCentre = target.Feet + Vector3.up * .65f;
            Vector3 ray = targetCentre - origin;
            foreach (var obstacle in Physics.RaycastAll(origin, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!obstacle.collider.GetComponentInParent<TileActor>()) { blocked = true; break; }
            if (blocked) continue;
            hit.Add(target);
            hitbox.HitActor(target, forward);
        }
    }
    void OnDestroy() { if (material) Destroy(material); }
}
