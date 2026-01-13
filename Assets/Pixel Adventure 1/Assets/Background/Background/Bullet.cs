using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float lifeTime = 2f;
    [SerializeField] private int damage = 1;

    private Rigidbody2D rb;

    public void Init(Vector2 velocity)
    {
        rb = GetComponent<Rigidbody2D>();
        rb.linearVelocity = velocity;
        Destroy(gameObject, lifeTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Don’t hit the player (simple filter)
        if (other.CompareTag("Player")) return;

        // If enemy has a health script, call it (optional)
        // other.GetComponent<EnemyHealth>()?.TakeDamage(damage);

        Destroy(gameObject);
    }
}
