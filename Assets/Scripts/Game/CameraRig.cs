using UnityEngine;

namespace Jam
{
    /// <summary>Top-down angled follow camera. No rotation, so world-axis WASD reads naturally.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 22f;
        public float Pitch = 60f;
        public float Smoothing = 8f;

        Vector3 _offset;

        void Awake()
        {
            RecomputeOffset();
        }

        void RecomputeOffset()
        {
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            _offset = rot * new Vector3(0f, 0f, -Distance);
        }

        public void Snap()
        {
            RecomputeOffset();
            if (Target == null) return;
            transform.SetPositionAndRotation(Target.position + _offset, Quaternion.Euler(Pitch, 0f, 0f));
        }

        void LateUpdate()
        {
            if (Target == null) return;

            Vector3 desired = Target.position + _offset;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-Smoothing * Time.deltaTime));
            transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }
}
