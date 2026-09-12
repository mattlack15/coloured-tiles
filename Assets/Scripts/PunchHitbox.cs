using System;
using UnityEngine;

public class PunchHitbox : MonoBehaviour
{
    [Header("KnockbackSettings")]
    [SerializeField] private float knockbackForce = 10f;

    public void HitActor(TileActor target, Vector3 direction)
    {
        target.ReceivePunch(direction * knockbackForce, knockbackForce * .5f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            Rigidbody enemyRb = other.GetComponent<Rigidbody>();
            if (enemyRb != null)
            {
                Vector3 knockbackDirection = (other.transform.position - transform.position).normalized;
                knockbackDirection.y = 0;
                Vector3 finalForce = (knockbackDirection * knockbackForce) + Vector3.up * (knockbackForce * 0.5f);
                enemyRb.AddForce(finalForce, ForceMode.VelocityChange);


                Debug.Log($"Hit enemy!");
            }
        }
    }
}
