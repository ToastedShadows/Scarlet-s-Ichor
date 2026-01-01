using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator anim;

    private enum MovementState { idle, running, jumping, falling, dashing }
    private MovementState state;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 50f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private int maxJumps = 2;

    [Header("Variable Jump")]
    [SerializeField] private float jumpCutMultiplier = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.12f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.5f;
    [SerializeField] private bool refreshDashOnGround = true;
    [SerializeField] private bool cancelDashOnWallHit = true;

    [Header("Dash Cancel")]
    [SerializeField] private bool cancelDashOnTurn = true;

    [Header("Down Dash")]
    [SerializeField] private float downDashAngle = 45f;
    [SerializeField] private float downDashMultiplier = 1.5f;

    [Header("Dash Gravity")]
    [SerializeField] private float dashGravityMultiplier = 2f;
    [SerializeField] private float downDashGravityMultiplier = 3.5f;

    [Header("Jump Feel")]
    [SerializeField] private float apexVelocityThreshold = 1.5f;
    [SerializeField] private float apexGravityMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float maxFallSpeed = -18f;

    [Header("Animation")]
    [SerializeField] private float dashAnimationLength = 0.5f;

    private int jumpsRemaining;
    private bool isGrounded;

    private float horizontal;
    private bool downHeld;

    private bool isDashing;
    private bool isDownDash;
    private float dashTimeLeft;
    private float dashCooldownLeft;
    private Vector2 dashVelocity;
    private float dashDirSign;

    private float baseGravity;
    private const float EPS = 0.01f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();

        baseGravity = rb.gravityScale;
        jumpsRemaining = maxJumps;
    }

    void Update()
    {
        // Input
        horizontal =
            (Keyboard.current.rightArrowKey.isPressed ? 1f : 0f) +
            (Keyboard.current.leftArrowKey.isPressed ? -1f : 0f);

        downHeld = Keyboard.current.downArrowKey.isPressed;

        // Ground check
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        if (isGrounded)
        {
            jumpsRemaining = maxJumps;
            if (refreshDashOnGround) dashCooldownLeft = 0f;
        }

        // Flip sprite
        if (horizontal > EPS) sr.flipX = false;
        else if (horizontal < -EPS) sr.flipX = true;

        // Jump press
        if (Keyboard.current.zKey.wasPressedThisFrame && jumpsRemaining > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpsRemaining--;
        }

        // Variable jump cut
        if (Keyboard.current.zKey.wasReleasedThisFrame && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier
            );
        }

        // Dash start
        if (Keyboard.current.cKey.wasPressedThisFrame && !isDashing && dashCooldownLeft <= 0f)
        {
            StartDash(downHeld);
        }

        // 🔥 Cancel dash if player turns around
        if (isDashing && cancelDashOnTurn && Mathf.Abs(horizontal) > EPS)
        {
            if (Mathf.Sign(horizontal) != dashDirSign)
            {
                EndDash();
            }
        }

        UpdateAnimationState();
    }

    void FixedUpdate()
    {
        if (dashCooldownLeft > 0f)
            dashCooldownLeft -= Time.fixedDeltaTime;

        if (isDashing)
        {
            DashTick();
            return;
        }

        // Horizontal ramping
        float targetX = horizontal * moveSpeed;
        float rate = Mathf.Abs(targetX) > EPS ? acceleration : deceleration;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

        ApplyBetterJumpPhysics();
    }

    private void DashTick()
    {
        dashTimeLeft -= Time.fixedDeltaTime;

        float newX = dashVelocity.x;
        float newY = rb.linearVelocity.y;

        if (isDownDash)
            newY += dashVelocity.y * Time.fixedDeltaTime;

        rb.linearVelocity = new Vector2(newX, newY);

        rb.gravityScale = baseGravity *
            (isDownDash ? downDashGravityMultiplier : dashGravityMultiplier);

        if (dashTimeLeft <= 0f)
            EndDash();
    }

    private void StartDash(bool downward)
    {
        float facingDir =
            Mathf.Abs(horizontal) > EPS ? Mathf.Sign(horizontal) :
            (sr.flipX ? -1f : 1f);

        dashDirSign = Mathf.Sign(facingDir);
        isDownDash = downward;

        if (downward)
        {
            float rad = downDashAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad) * facingDir, -Mathf.Sin(rad)).normalized;
            dashVelocity = new Vector2(dir.x * dashSpeed, dir.y * dashSpeed * downDashMultiplier);
        }
        else
        {
            dashVelocity = new Vector2(facingDir * dashSpeed, 0f);
        }

        isDashing = true;
        dashTimeLeft = dashDuration;

        anim.speed = dashAnimationLength / Mathf.Max(dashDuration, 0.0001f);
    }

    private void EndDash()
    {
        isDashing = false;
        isDownDash = false;
        dashCooldownLeft = dashCooldown;

        rb.gravityScale = baseGravity;
        anim.speed = 1f;

        rb.linearVelocity = new Vector2(
            Mathf.Clamp(rb.linearVelocity.x, -moveSpeed, moveSpeed),
            rb.linearVelocity.y
        );
    }

    private void ApplyBetterJumpPhysics()
    {
        if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = baseGravity * fallGravityMultiplier;

            if (rb.linearVelocity.y < maxFallSpeed)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, maxFallSpeed);
        }
        else if (Mathf.Abs(rb.linearVelocity.y) < apexVelocityThreshold)
        {
            rb.gravityScale = baseGravity * apexGravityMultiplier;
        }
        else
        {
            rb.gravityScale = baseGravity;
        }
    }

    private void UpdateAnimationState()
    {
        if (isDashing) state = MovementState.dashing;
        else if (rb.linearVelocity.y > 0.1f) state = MovementState.jumping;
        else if (rb.linearVelocity.y < -0.1f) state = MovementState.falling;
        else if (Mathf.Abs(horizontal) > EPS) state = MovementState.running;
        else state = MovementState.idle;

        anim.SetInteger("State", (int)state);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isDashing || !cancelDashOnWallHit) return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector2 n = collision.GetContact(i).normal;
            if (Mathf.Abs(n.x) > 0.5f)
            {
                EndDash();
                break;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
