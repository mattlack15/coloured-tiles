using UnityEngine;
using UnityEngine.InputSystem;

public class CastAimIndicator : MonoBehaviour
{
    [Header("Components")]
    public LineRenderer lineRenderer;
    [SerializeField] private InputActionReference aimAction;
    [SerializeField] private Transform punchTransform;

    [Header("Settings")]
    public float maxCastRange = 5f;
    [Range(0.01f, 1f)] public float analogDeadzone = 0.2f; 

    private Vector3 lastAimDirection = Vector3.forward;

    void OnEnable()
    {
        if (aimAction != null && aimAction.action != null)
        {
            aimAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (aimAction != null && aimAction.action != null)
        {
            aimAction.action.Disable();
        }
    }

    void Start()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = true;
        }
    }

    void Update()
    {
        UpdateInputSystemAim();
    }

    void UpdateInputSystemAim()
    {
        if (lineRenderer == null || aimAction == null || aimAction.action == null) return;

        Vector2 stickInput = aimAction.action.ReadValue<Vector2>();
        Vector3 currentInputDirection = new Vector3(stickInput.x, 0f, stickInput.y);

        if (currentInputDirection.magnitude > analogDeadzone)
        {
            lastAimDirection = currentInputDirection.normalized;
        }

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + (lastAimDirection * maxCastRange);

        lineRenderer.SetPosition(0, startPosition);
        lineRenderer.SetPosition(1, targetPosition);

        if (punchTransform != null)
        {
            punchTransform.position = targetPosition;

            if (lastAimDirection != Vector3.zero)
            {
                punchTransform.rotation = Quaternion.LookRotation(lastAimDirection);
            }
        }
    }
}
