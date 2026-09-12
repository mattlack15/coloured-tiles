using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class TestPlayer : MonoBehaviour
{
    public float speed = 6;
    public float jumpHeight = 1.6f;
    CharacterController controller;
    float vertical;
    bool dead;
    public bool IsDead => dead;
    void Awake() { controller = GetComponent<CharacterController>(); }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (dead) return;
        if (controller.isGrounded && vertical < 0) vertical = -2;
        if (controller.isGrounded && Input.GetKeyDown(KeyCode.Space)) vertical = Mathf.Sqrt(jumpHeight * 40);
        vertical -= 20 * Time.deltaTime;
        float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        float z = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
        Vector3 move = Vector3.ClampMagnitude(new Vector3(x,0,z),1) * speed;
        controller.Move((move + Vector3.up * vertical) * Time.deltaTime);
        if (transform.position.y < -9) Die();
    }
    public void Die() { dead = true; }
    void OnGUI() { if (dead) GUI.Box(new Rect(Screen.width / 2 - 150,Screen.height / 2 - 35,300,70), "You fell!\nPress R to restart the round."); }
}
