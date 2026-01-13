using UnityEngine;
using UnityEngine.InputSystem;

public class Gun : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private SpriteRenderer playerSprite; // to know facing

    [Header("Gun")]
    [SerializeField] private float bulletSpeed = 18f;
    [SerializeField] private float fireCooldown = 0.12f;

    private float cooldownLeft;

    private void Update()
    {
        if (cooldownLeft > 0f)
            cooldownLeft -= Time.deltaTime;

        // Left mouse to shoot
        if (Mouse.current.leftButton.wasPressedThisFrame && cooldownLeft <= 0f)
        {
            Shoot();
        }
    }

    private void Shoot()
    {
        cooldownLeft = fireCooldown;

        float dir = (playerSprite != null && playerSprite.flipX) ? -1f : 1f;
        Vector2 velocity = new Vector2(dir * bulletSpeed, 0f);

        Bullet b = Instantiate(bulletPrefab, firePoint.position, Quaternion.identity);
        b.Init(velocity);
    }
}
