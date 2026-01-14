using UnityEngine;
using UnityEngine.InputSystem;

public class Gun : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private Camera cam;
    [SerializeField] private PlayerMovement playerMovement; // drag your Player (with PlayerMovement)

    [Header("Gun")]
    [SerializeField] private float bulletSpeed = 18f;
    [SerializeField] private float fireCooldown = 0.12f;

    [Header("Placement")]
    [SerializeField] private Vector2 gunOffset = new Vector2(0.45f, 0.15f);

    private float cooldown;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (cooldown > 0f) cooldown -= Time.deltaTime;
        if (!cam || !firePoint || !bulletPrefab || !playerMovement) return;

        Vector2 mouseWorld = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        // ✅ Flip player based on cursor position relative to player
        bool faceLeft = mouseWorld.x < playerMovement.transform.position.x;
        playerMovement.SetFacing(faceLeft);

        float side = faceLeft ? -1f : 1f;

        // ✅ Move gun to correct side of body
        transform.localPosition = new Vector3(gunOffset.x * side, gunOffset.y, transform.localPosition.z);

        // ✅ Aim direction from firePoint to mouse
        Vector2 aimDir = mouseWorld - (Vector2)firePoint.position;

        // ❌ Don’t allow aiming backwards through the body
        if (side > 0f && aimDir.x < 0f) aimDir.x = 0.001f;
        if (side < 0f && aimDir.x > 0f) aimDir.x = -0.001f;

        if (aimDir.sqrMagnitude < 0.0001f)
            aimDir = Vector2.right * side;

        aimDir.Normalize();

        // ✅ Rotate gun to aim
        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // 🔫 Shoot
        if (Mouse.current.leftButton.wasPressedThisFrame && cooldown <= 0f)
        {
            cooldown = fireCooldown;
            Bullet b = Instantiate(bulletPrefab, firePoint.position, Quaternion.identity);
            b.Init(aimDir * bulletSpeed);
        }
    }
}
