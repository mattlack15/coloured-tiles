using UnityEngine;

// Human input and bot decisions both feed this motor. Only FloatingMap steps it.
[RequireComponent(typeof(CharacterController))]
public class TileActor : MonoBehaviour
{
    public const float Radius = .28f;
    public const float Height = 1.05f;
    public const float Speed = 6f;
    public const float JumpHeight = 1.6f;
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
    public Vector3 CommandVelocity => IsDead || IsLaunched ? Vector3.zero : intent * MoveSpeed;
    public Vector3 Feet => transform.position - Vector3.up * (Height * .5f);
    public FloatingMap Map { get; set; }
    CharacterController controller;
    Material bodyMaterial;
    Renderer bodyRenderer;
    Vector3 intent, walking, shove;
    Vector3 launchVelocity;
    float launchSpeed = 10, launchUpSpeed = 24;
    float vertical;
    bool jump;

    void Awake()
    {
        gameObject.layer = 2; // Navigation queries only see terrain.
        controller = GetComponent<CharacterController>();
        controller.height = Height;
        controller.radius = Radius;
        controller.center = Vector3.zero;
        controller.skinWidth = .015f;
        controller.stepOffset = .22f;
        controller.minMoveDistance = 0;
        var oldVisual = GetComponent<Renderer>();
        if (oldVisual) oldVisual.enabled = false;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Body";
        visual.layer = 2;
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = new Vector3(Radius * 2, Height * .5f, Radius * 2);
        var collider = visual.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        bodyMaterial = new Material(Shader.Find("Unlit/Color"));
        bodyMaterial.color = Color.white;
        bodyRenderer = visual.GetComponent<Renderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;
    }

    public void SetInput(Vector3 direction, bool jumpPressed = false)
    {
        intent = IsDead || IsLaunched ? Vector3.zero : Vector3.ClampMagnitude(new Vector3(direction.x, 0, direction.z), 1);
        jump |= jumpPressed && !IsDead && !IsLaunched;
    }
    public void ConfigureMovement(float speed, float jumpHeight, float outward, float upward)
    {
        MoveSpeed = Mathf.Max(.1f, speed);
        JumpHeightValue = Mathf.Max(0, jumpHeight);
        launchSpeed = outward;
        launchUpSpeed = upward;
    }
    public void ClearAssignment()
    {
        ColourIndex = -1;
        Target = null;
        Result = "";
        SetInput(Vector3.zero);
        bodyMaterial.color = Color.white;
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
        bodyRenderer.enabled = true;
        Result = "";
        intent = walking = shove = Velocity = Vector3.zero;
        vertical = 0;
        jump = false;
        bodyMaterial.color = Color.white;
    }
    public void Assign(int colour, Renderer target, Vector3 slot, Color tint)
    {
        ColourIndex = colour;
        Target = target;
        TargetPosition = slot;
        bodyMaterial.color = tint;
    }
    public void SetTint(Color colour) { bodyMaterial.color = colour; }
    public void SetStandingPosition(Vector3 position)
    {
        if (!Target) return;
        Bounds bounds = Target.bounds;
        float margin = Radius + .08f;
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
        jump = false;
        bodyMaterial.color = new Color(.3f, .32f, .36f);
        bodyRenderer.enabled = false;
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
        if (jump && Grounded && !IsLaunched) vertical = Mathf.Sqrt(2 * Gravity * JumpHeightValue);
        jump = false;
        vertical -= Gravity * dt;
        walking = Vector3.MoveTowards(walking, intent * MoveSpeed, 32 * dt);
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
        if (distance > Radius * 2 + .1f || distance < .001f) return;
        Vector3 normal = delta / distance;
        float closing = Vector3.Dot(a.CommandVelocity - b.CommandVelocity, normal);
        if (closing <= 0) return;
        Vector3 impulse = normal * Mathf.Min(closing, Mathf.Min(a.MoveSpeed, b.MoveSpeed)) * 6 * dt;
        a.AddShove(-impulse);
        b.AddShove(impulse);
    }
    void OnDestroy() { if (bodyMaterial) Destroy(bodyMaterial); }
}
