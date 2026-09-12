using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class TestPlayer : MonoBehaviour
{
    public float speed = 6;
    public float launchSpeed = 18;
    public float launchUpSpeed = 9;
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
        if (dead) return;
        if (!launched && controller.isGrounded && vertical < 0) vertical = -2;
        // Jumping is deliberately gone. It let a player hop over the crowd and, worse, hop off a
        // dropped tile and back on before the judgement, which dodged the whole round.
        vertical -= 20 * Time.deltaTime;
        Vector2 input = Jam.InputBridge.Move;
        Vector3 move = launched ? launchVelocity : Vector3.ClampMagnitude(new Vector3(input.x,0,input.y),1) * speed;
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
    void OnGUI() { if (dead) GUI.Box(new Rect(Screen.width / 2 - 180,Screen.height / 2 - 35,360,70), "Life lost!\nRespawning on the edge next round."); }
}
