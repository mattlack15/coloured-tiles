using UnityEngine;

// Human input and bot decisions both feed this motor. Only FloatingMap steps it.
[RequireComponent(typeof(CharacterController))]
public class TileActor : MonoBehaviour
{
    public const float Radius = .5f;
    public const float Height = 2f;
    public const float Speed = 6f;
    public const float JumpHeight = 0f;
    public const float Gravity = 20f;
    public string DisplayName = "YOU";
    public string PersonalityName = "";
    public int ColourIndex { get; private set; } = -1;
    public Renderer Target { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsLaunched { get; private set; }
    public int LivesRemaining { get; private set; } = 3;
    public bool IsEliminated => LivesRemaining == 0;
    public float MoveSpeed { get; private set; } = Speed;
    public float JumpHeightValue { get; private set; } = JumpHeight;
    public string Result { get; private set; } = "";
    public int Score { get; private set; }
    public bool Grounded => controller && controller.isGrounded;
    public Vector3 Velocity { get; private set; }
    public Vector3 PushVelocity => shove;
    public Vector3 CommandVelocity => IsDead || IsLaunched ? Vector3.zero : intent * MoveSpeed;
    public Vector3 Feet => controller.bounds.center - Vector3.up * controller.bounds.extents.y;
    public float BodyRadius => controller.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
    public FloatingMap Map { get; set; }
    CharacterController controller;
    Material bodyMaterial;
    Renderer[] bodyRenderers;
    MaterialPropertyBlock colourBlock;
    Vector3 intent, walking, shove;
    Vector3 launchVelocity;
    float launchSpeed = 10, launchUpSpeed = 24;
    float vertical;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        // Preserve the human's existing renderer, transform and controller settings.
        // New bots use a normal full-size capsule, just like the original player.
        bodyRenderers = GetComponentsInChildren<Renderer>();
        if (bodyRenderers.Length == 0)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(transform, false);
            var collider = visual.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            bodyRenderers = GetComponentsInChildren<Renderer>();
        }
        // Per-character shading keeps overlapping silhouettes readable without scene lights.
        bodyMaterial = new Material(Resources.Load<Shader>("CharacterColour"));
        bodyMaterial.color = Color.white;
        foreach (var renderer in bodyRenderers)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = bodyMaterial;
            renderer.sharedMaterials = materials;
        }
        colourBlock = new MaterialPropertyBlock();
    }

    public void SetInput(Vector3 direction, bool jumpPressed = false)
    {
        intent = IsDead || IsLaunched ? Vector3.zero : Vector3.ClampMagnitude(new Vector3(direction.x, 0, direction.z), 1);
    }
    public void ConfigureMovement(float speed, float jumpHeight, float outward, float upward)
    {
        MoveSpeed = Mathf.Max(.1f, speed);
        JumpHeightValue = 0;
        launchSpeed = outward;
        launchUpSpeed = upward;
    }
    public void ClearAssignment()
    {
        ColourIndex = -1;
        Target = null;
        Result = "";
        SetInput(Vector3.zero);
    }
    public void ResetRound(Vector3 position)
    {
        if (IsEliminated) return;
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        ColourIndex = -1;
        Target = null;
        IsDead = false;
        IsLaunched = false;
        launchVelocity = Vector3.zero;
        foreach (var renderer in bodyRenderers) renderer.enabled = true;
        Result = "";
        intent = walking = shove = Velocity = Vector3.zero;
        vertical = -2;
    }
    public void Assign(int colour, Renderer target, Vector3 slot, Color tint)
    {
        ColourIndex = colour;
        Target = target;
        TargetPosition = slot;
        SetTint(tint);
    }
    public void SetTint(Color colour)
    {
        foreach (var renderer in bodyRenderers)
        {
            renderer.GetPropertyBlock(colourBlock);
            colourBlock.SetColor("_Color", colour);
            colourBlock.SetColor("_BaseColor", colour);
            renderer.SetPropertyBlock(colourBlock);
        }
    }
    public void SetStandingPosition(Vector3 position)
    {
        if (!Target) return;
        Bounds bounds = Target.bounds;
        float margin = BodyRadius - .04f;
        TargetPosition = new Vector3(Mathf.Clamp(position.x, bounds.min.x + margin, bounds.max.x - margin),
            bounds.max.y, Mathf.Clamp(position.z, bounds.min.z + margin, bounds.max.z - margin));
    }
    public bool IsOnTarget()
    {
        if (IsDead || IsLaunched || !Target || !Target.gameObject.activeSelf) return false;
        Bounds bounds = Target.bounds;
        Vector3 feet = Feet;
        return feet.x >= bounds.min.x + Radius * .6f && feet.x <= bounds.max.x - Radius * .6f
            && feet.z >= bounds.min.z + Radius * .6f && feet.z <= bounds.max.z - Radius * .6f
            && feet.y >= bounds.max.y - .2f && feet.y <= bounds.max.y + 4;
    }
    public void MarkSafe()
    {
        Score++; Result = "Safe!";
        SetInput(Vector3.zero);
    }
    public void LaunchOff(Vector3 centre)
    {
        if (IsDead || IsLaunched) return;
        Vector3 direction = transform.position - centre; direction.y = 0;
        if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
        launchVelocity = direction.normalized * launchSpeed;
        vertical = launchUpSpeed;
        IsLaunched = true;
        intent = walking = shove = Vector3.zero;
        Result = "Wrong colour!";
    }
    public void Die(string reason = "You fell")
    {
        if (IsDead) return;
        IsDead = true;
        LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
        Result = reason;
        intent = walking = Vector3.zero;
        foreach (var renderer in bodyRenderers) renderer.enabled = false;
        controller.enabled = false;
    }
    public void AddShove(Vector3 impulse)
    {
        if (!IsDead && !IsLaunched) shove = Vector3.ClampMagnitude(shove + impulse, MoveSpeed * .7f);
    }
    public void Step(float dt)
    {
        if (!isActiveAndEnabled || IsDead) { Velocity = Vector3.zero; return; }
        Vector3 before = transform.position;
        if (!IsLaunched && Grounded && vertical < 0) vertical = -2;
        vertical -= Gravity * dt;
        // Match the original player's immediate input response. Only contact adds displacement.
        walking = intent * MoveSpeed;
        Vector3 horizontal = IsLaunched ? launchVelocity : walking + shove;
        controller.Move((horizontal + Vector3.up * vertical) * dt);
        shove = Vector3.MoveTowards(shove, Vector3.zero, 8 * dt);
        Velocity = (transform.position - before) / dt;
        if (transform.position.y < -9) Die();
    }
    // Equal and opposite contact impulses; neither the human nor a preset gets priority.
    public static void ResolveContact(TileActor a, TileActor b, float dt)
    {
        if (!a.isActiveAndEnabled || !b.isActiveAndEnabled || a.IsDead || b.IsDead || a.IsLaunched || b.IsLaunched) return;
        if (Mathf.Abs(a.transform.position.y - b.transform.position.y) > Height * .85f) return;
        Vector3 delta = b.transform.position - a.transform.position;
        delta.y = 0;
        float distance = delta.magnitude;
        if (distance > a.BodyRadius + b.BodyRadius + .1f || distance < .001f) return;
        Vector3 normal = delta / distance;
        float closing = Vector3.Dot(a.CommandVelocity - b.CommandVelocity, normal);
        if (closing <= 0) return;
        Vector3 impulse = normal * Mathf.Min(closing, Mathf.Min(a.MoveSpeed, b.MoveSpeed)) * 6 * dt;
        a.AddShove(-impulse);
        b.AddShove(impulse);
    }
    void OnDestroy() { if (bodyMaterial) Destroy(bodyMaterial); }
}
