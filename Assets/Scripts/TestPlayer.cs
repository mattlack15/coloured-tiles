using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class TestPlayer : MonoBehaviour
{
    public bool ControlsEnabled { get; set; }
    public float speed = 6;
    [HideInInspector] public float jumpHeight = 0;
    public float launchSpeed = 10;
    public float launchUpSpeed = 24;
    public TileActor Actor { get; private set; }
    public bool IsDead => Actor && Actor.IsDead;
    public bool IsEliminated => Actor && Actor.IsEliminated;
    public int LivesRemaining => Actor ? Actor.LivesRemaining : 3;
    public Vector3 Feet => Actor ? Actor.Feet : transform.position;
    void Awake()
    {
        Actor = GetComponent<TileActor>();
        if (!Actor) Actor = gameObject.AddComponent<TileActor>();
        if (!GetComponent<PlayerPunch>()) gameObject.AddComponent<PlayerPunch>();
        Actor.DisplayName = "YOU";
        Actor.ConfigureMovement(speed, jumpHeight, launchSpeed, launchUpSpeed);
    }
    void Update()
    {
        if (!ControlsEnabled) return;
        if (Input.GetKeyDown(KeyCode.R)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (!Actor) return;
        float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        float z = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
        Actor.SetInput(new Vector3(x,0,z));
    }
    public void Die() { if (Actor) Actor.Die(); }
    public void Respawn(Vector3 position) { if (Actor) Actor.ResetRound(position); }
    public void LaunchOff(Vector3 centre) { if (Actor) Actor.LaunchOff(centre); }
    public void SetTargetColour(Color colour) { if (Actor) Actor.SetTint(colour); }
    void OnGUI() { if (IsDead) GUI.Box(new Rect(Screen.width / 2 - 180,Screen.height / 2 - 35,360,70), IsEliminated ? "Game over!\nPress R to restart with 3 lives." : "Life lost!\nRespawning on the edge next round."); }
}
