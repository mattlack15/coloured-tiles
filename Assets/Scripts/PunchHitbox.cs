using UnityEngine;

namespace Jam
{
    /// <summary>
    /// The business end of a punch, ported from the playerLogic branch.
    ///
    /// Two things had to change to work here, and both are deliberate:
    ///
    ///  - The original knocked targets back with Rigidbody.AddForce. The crowd's bodies are
    ///    KINEMATIC in this project, on purpose: that is what stops them shoving the player through
    ///    PhysX and lifting the capsule. AddForce does nothing at all to a kinematic body, so the
    ///    knockback is delivered through the same Agent.Move channel as ordinary shoving - just far
    ///    stronger. A punch therefore carries someone clean off the board, which is the point.
    ///
    ///  - The original tested other.CompareTag("Enemy"). There is no Enemy tag here; the crowd is a
    ///    layer, so targets are selected by LayerMask.
    ///
    /// Fired explicitly rather than relying on OnTriggerEnter, so the punch lands once per press
    /// instead of continuously while the box is armed.
    /// </summary>
    public class PunchHitbox : MonoBehaviour
    {
        [Header("Knockback")]
        [Tooltip("Knockback speed in metres per second, decayed over the next fraction of a second. This is the punch's whole payload.")]
        [SerializeField] private float knockbackSpeed = 11f;

        [Tooltip("Which layer counts as a target. The crowd sits on this one.")]
        public LayerMask TargetMask;

        [Tooltip("Radius used when no SphereCollider is present.")]
        public float FallbackRadius = 0.9f;

        readonly Collider[] _hits = new Collider[32];

        /// <summary>Punch everything currently inside the hitbox.</summary>
        public void Fire()
        {
            float radius = FallbackRadius;
            if (GetComponent<Collider>() is SphereCollider sphere)
                radius = sphere.radius * Mathf.Max(0.001f, transform.lossyScale.x);

            int n = Physics.OverlapSphereNonAlloc(transform.position, radius, _hits,
                                                  TargetMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var rb = _hits[i].attachedRigidbody;
                if (rb == null || rb.transform == transform) continue;

                var loco = rb.GetComponent<NpcLocomotion>();
                if (loco == null) continue;

                Vector3 dir = rb.transform.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

                loco.Punch(dir.normalized, knockbackSpeed);
            }
        }
    }
}
