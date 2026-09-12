using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class TestPlayer : MonoBehaviour
{
    /// <summary>Gate for the title screen: false until Start is pressed, so nothing moves behind it.</summary>
    public bool ControlsEnabled { get; set; }
    public float speed = 6;
    public float launchSpeed = 10;
    public float launchUpSpeed = 24;
    CharacterController controller;
    float vertical;
    bool dead;
    bool launched;
    Vector3 launchVelocity;
    public bool IsDead => dead;
    public int LivesLost { get; private set; }
    public Vector3 Feet => controller.bounds.center - Vector3.up * controller.bounds.extents.y;
    void Awake() { controller = GetComponent<CharacterController>(); }
    void Update()
    {
        if (Jam.InputBridge.RestartPressed) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (!ControlsEnabled) return;
        if (dead) return;
        if (!launched && controller.isGrounded && vertical < 0) vertical = -2;
        // Jumping is deliberately gone. It let a player hop over the crowd and, worse, hop off a
        // dropped tile and back on before the judgement, which dodged the whole round.
        vertical -= 20 * Time.deltaTime;
        Vector2 input = Jam.InputBridge.Move;
        Vector3 move = launched ? launchVelocity : Vector3.ClampMagnitude(new Vector3(input.x,0,input.y),1) * speed;

        // Face the way we are going, so the body and the punch agree about which way is forward.
        Vector3 facing = new Vector3(input.x, 0f, input.y);
        if (facing.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(facing.normalized, Vector3.up), 14f * Time.deltaTime);
        controller.Move((move + Vector3.up * vertical) * Time.deltaTime);
        if (transform.position.y < -9) Die();
    }
    public void LaunchOff(Vector3 mapCentre)
    {
        if (dead || launched) return;
        Vector3 direction = transform.position - mapCentre; direction.y = 0;
        if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
        launchVelocity = direction.normalized * launchSpeed; vertical = launchUpSpeed; launched = true;
    }
    public void Die()
    {
        if (dead) return;
        dead = true; LivesLost++;
        controller.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }
    public void Respawn(Vector3 position)
    {
        controller.enabled = false; transform.position = position;
        vertical = -2; launchVelocity = Vector3.zero; launched = false; dead = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        controller.enabled = true;
    }
    GUIStyle _lifeLostStyle;

    void OnGUI()
    {
        if (!dead) return;

        // normal.textColor set explicitly: GUI.skin.label ships with a DARK default text colour
        // because it is designed for light backgrounds, and GUI.color multiplies with it.
        if (_lifeLostStyle == null)
        {
            _lifeLostStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white },
            };
        }

        var box = new Rect(Screen.width * 0.5f - 440f, Screen.height * 0.5f - 110f, 880f, 220f);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(box, "LIFE LOST\nRespawning on the edge next round", _lifeLostStyle);
    }
}
