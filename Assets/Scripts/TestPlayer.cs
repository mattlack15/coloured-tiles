using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class TestPlayer : MonoBehaviour
{
    public float speed = 6;
    public float jumpHeight = 1.6f;
    public float launchSpeed = 10;
    public float launchUpSpeed = 24;
    CharacterController controller;
    float vertical;
    bool dead;
    bool launched;
    Vector3 launchVelocity;
    public bool IsDead => dead;
    public int LivesRemaining { get; private set; } = 3;
    public bool IsEliminated => LivesRemaining == 0;
    MaterialPropertyBlock colourBlock;
    Renderer[] bodyRenderers;
    public Vector3 Feet => controller.bounds.center - Vector3.up * controller.bounds.extents.y;
    void Awake()
    {
        controller = GetComponent<CharacterController>();
        bodyRenderers = GetComponentsInChildren<Renderer>();
        colourBlock = new MaterialPropertyBlock();
    }
    public void SetTargetColour(Color colour)
    {
        foreach (var r in bodyRenderers)
        {
            r.GetPropertyBlock(colourBlock);
            colourBlock.SetColor("_Color", colour);
            colourBlock.SetColor("_BaseColor", colour);
            r.SetPropertyBlock(colourBlock);
        }
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (dead) return;
        if (!launched && controller.isGrounded && vertical < 0) vertical = -2;
        if (!launched && controller.isGrounded && Input.GetKeyDown(KeyCode.Space)) vertical = Mathf.Sqrt(jumpHeight * 40);
        vertical -= 20 * Time.deltaTime;
        float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        float z = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
        Vector3 move = launched ? launchVelocity : Vector3.ClampMagnitude(new Vector3(x,0,z),1) * speed;
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
        dead = true; LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
        controller.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }
    public void Respawn(Vector3 position)
    {
        if (IsEliminated) return;
        controller.enabled = false; transform.position = position;
        vertical = -2; launchVelocity = Vector3.zero; launched = false; dead = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        controller.enabled = true;
    }
    void OnGUI() { if (dead) GUI.Box(new Rect(Screen.width / 2 - 180,Screen.height / 2 - 35,360,70), IsEliminated ? "Game over!\nPress R to restart with 3 lives." : "Life lost!\nRespawning on the edge next round."); }
}
