using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference punchAction;

    private Vector2 moveInput;
    private Transform playerTransform;
    private Rigidbody rb;

    public void OnPunch()
    {
        // Implement punch logic here
        Debug.Log("Punch action triggered!");
        Transform punchTransform = transform.Find("AimIndicator/PlayerPunch");

        if (punchTransform != null)
        {
            GameObject punch = punchTransform.gameObject;
            StartCoroutine(PunchDurationRoutine(punch, 0.2f));
        } else
        {
            Debug.LogError("Punch Transform not found! Ensure the hierarchy is correct.");
        }
    }

    private System.Collections.IEnumerator PunchDurationRoutine(GameObject punch, float duration)
    {
        punch.SetActive(true);
        yield return new WaitForSeconds(duration);
        punch.SetActive(false);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        playerTransform = GetComponent<Transform>();
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        if (moveAction != null)
        {
            moveInput = moveAction.action.ReadValue<Vector2>();
        }
        
        if (punchAction != null && punchAction.action.WasPressedThisFrame())
        {
            OnPunch();
        }
    }

    void FixedUpdate()
    {
        Vector3 movement = new Vector3(moveInput.x, 0f, moveInput.y) * speed * Time.fixedDeltaTime;
        rb.MovePosition(playerTransform.position + movement);
    }
}
