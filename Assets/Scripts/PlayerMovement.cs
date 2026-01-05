using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator anim;
    private Collider2D selfCol;

    private enum MovementState { idle, running, jumping, falling, rolling }
    private MovementState state;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 50f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private int maxJumps = 2;

    [Header("Jump Buffer")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Coyote Time")]
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Variable Jump")]
    [SerializeField] private float jumpCutMultiplier = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.12f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Wall Check")]
    [SerializeField] private Transform wallCheck;
    [SerializeField] private Vector2 wallCheckOffset = new Vector2(0.35f, 0f);
    [SerializeField] private float wallCheckRadius = 0.14f;
    [SerializeField] private LayerMask wallLayer;

    [Header("Wall Slide")]
    [SerializeField] private float wallSlideMaxFallSpeed = -4f;
    [SerializeField] private float wallSlideGravityMultiplier = 0.5f;

    [Header("Wall Jump")]
    [SerializeField] private float wallJumpForce = 12f;
    [SerializeField] private float wallJumpHorizontalForce = 8f;
    [SerializeField] private float wallJumpLockTime = 0.15f;
    [SerializeField] private int refillJumpsAfterWallJump = 1;

    [Header("Roll (Dash Replacement)")]
    [SerializeField] private float rollBoost = 8f;        // one-time horizontal boost
    [SerializeField] private float rollDuration = 0.18f;  // roll "state" time (animation / input lock)
    [SerializeField] private float rollCooldown = 0.5f;
    [SerializeField] private float rollControlMultiplier = 0.35f; // how much A/D works during roll (0 = locked, 1 = normal)
    [SerializeField] private float rollExtraDecel = 20f;  // extra X decel during roll (helps it feel like a roll)
    [SerializeField] private bool cancelRollOnTurn = false;

    [Header("Jump Feel")]
    [SerializeField] private float apexVelocityThreshold = 1.5f;
    [SerializeField] private float apexGravityMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float maxFallSpeed = -18f;

    private int jumpsRemaining;
    private bool isGrounded;

    private float horizontal;

    private float baseGravity;
    private const float EPS = 0.01f;

    // Jump buffer + coyote
    private float jumpBufferLeft;
    private float coyoteTimeLeft;

    // Wall
    private bool isTouchingWall;
    private bool isWallSliding;
    private float wallJumpDirX;

    // Wall jump lock
    private float wallJumpLockLeft;

    // Roll
    private bool isRolling;
    private float rollTimeLeft;
    private float rollCooldownLeft;
    private float rollDirSign;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();
        selfCol = GetComponent<Collider2D>();

        baseGravity = rb.gravityScale;
        jumpsRemaining = maxJumps;
    }

    void Update()
    {
        // ===== INPUT =====
        horizontal =
            (Keyboard.current.dKey.isPressed ? 1f : 0f) +
            (Keyboard.current.aKey.isPressed ? -1f : 0f);

        // Jump buffer (W)
        if (Keyboard.current.wKey.wasPressedThisFrame)
            jumpBufferLeft = jumpBufferTime;

        if (jumpBufferLeft > 0f)
            jumpBufferLeft -= Time.deltaTime;

        // Ground check + coyote
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        if (isGrounded)
        {
            jumpsRemaining = maxJumps;
            coyoteTimeLeft = coyoteTime;
        }
        else
        {
            coyoteTimeLeft -= Time.deltaTime;
        }

        // Flip sprite (don’t fight wall-jump lock)
        if (wallJumpLockLeft <= 0f)
        {
            if (horizontal > EPS) sr.flipX = false;
            else if (horizontal < -EPS) sr.flipX = true;
        }

        // Wall check moves with facing
        UpdateWallCheckPosition();
        ResolveWallContact();

        // Wall slide (disabled while rolling)
        isWallSliding =
            !isGrounded &&
            !isRolling &&
            isTouchingWall &&
            rb.linearVelocity.y < 0f;

        // Roll start (S)
        if (Keyboard.current.sKey.wasPressedThisFrame && !isRolling && rollCooldownLeft <= 0f)
        {
            StartRoll();
        }

        // Optional: cancel roll if opposite direction pressed
        if (isRolling && cancelRollOnTurn && Mathf.Abs(horizontal) > EPS)
        {
            if (Mathf.Sign(horizontal) != rollDirSign)
                EndRoll();
        }

        // Jump consume (wall jump priority)
        TryConsumeBufferedJump();

        // Variable jump cut (W release)
        if (Keyboard.current.wKey.wasReleasedThisFrame && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }

        UpdateAnimationState();
    }

    void FixedUpdate()
    {
        if (rollCooldownLeft > 0f)
            rollCooldownLeft -= Time.fixedDeltaTime;

        if (wallJumpLockLeft > 0f)
            wallJumpLockLeft -= Time.fixedDeltaTime;

        if (isRolling)
        {
            rollTimeLeft -= Time.fixedDeltaTime;
            if (rollTimeLeft <= 0f)
                EndRoll();
        }

        // Horizontal movement (reduced control during roll)
        float control = isRolling ? rollControlMultiplier : 1f;
        float inputX = wallJumpLockLeft > 0f ? 0f : (horizontal * control);

        float targetX = inputX * moveSpeed;
        float rate = Mathf.Abs(targetX) > EPS ? acceleration : deceleration;

        // Extra decel during roll so it doesn't feel like a dash
        if (isRolling && Mathf.Abs(targetX) < EPS)
            rate += rollExtraDecel;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

        // Wall slide override
        if (isWallSliding)
        {
            rb.gravityScale = baseGravity * wallSlideGravityMultiplier;

            if (rb.linearVelocity.y < wallSlideMaxFallSpeed)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, wallSlideMaxFallSpeed);

            return;
        }

        ApplyBetterJumpPhysics(); // ✅ gravity stays normal during roll (no glide)
    }

    // ===== ROLL =====
    private void StartRoll()
    {
        // Determine direction: input if held, otherwise facing
        float dir = (Mathf.Abs(horizontal) > EPS) ? Mathf.Sign(horizontal) : (sr.flipX ? -1f : 1f);
        rollDirSign = Mathf.Sign(dir);

        isRolling = true;
        rollTimeLeft = rollDuration;
        rollCooldownLeft = rollCooldown;

        // ✅ ONE-TIME BOOST ONLY (does NOT overwrite Y, does NOT keep forcing X)
        rb.linearVelocity = new Vector2(rb.linearVelocity.x + rollDirSign * rollBoost, rb.linearVelocity.y);
    }

    private void EndRoll()
    {
        isRolling = false;
    }

    // ===== JUMP LOGIC =====
    private void TryConsumeBufferedJump()
    {
        if (jumpBufferLeft <= 0f) return;

        // (Optional) allow jumping during roll; if you want roll to block jumps, add: if (isRolling) return;

        // Wall jump (allowed even if you used all double jumps)
        if (!isGrounded && isTouchingWall)
        {
            rb.linearVelocity = new Vector2(wallJumpDirX * wallJumpHorizontalForce, wallJumpForce);

            sr.flipX = wallJumpDirX < 0f;
            wallJumpLockLeft = wallJumpLockTime;
            jumpsRemaining = Mathf.Clamp(refillJumpsAfterWallJump, 0, maxJumps);

            jumpBufferLeft = 0f;
            coyoteTimeLeft = 0f;
            return;
        }

        // Normal jump (ground/coyote/air)
        bool canCoyote = coyoteTimeLeft > 0f && jumpsRemaining == maxJumps;

        if (jumpsRemaining > 0 || canCoyote)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            if (!canCoyote) jumpsRemaining--;

            jumpBufferLeft = 0f;
            coyoteTimeLeft = 0f;
        }
    }

    // ===== WALL CHECK =====
    private void UpdateWallCheckPosition()
    {
        if (!wallCheck) return;
        float side = sr.flipX ? -1f : 1f;
        wallCheck.position = (Vector2)transform.position + new Vector2(wallCheckOffset.x * side, wallCheckOffset.y);
    }

    private void ResolveWallContact()
    {
        isTouchingWall = false;
        wallJumpDirX = 0f;

        if (!wallCheck) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(wallCheck.position, wallCheckRadius, wallLayer);
        foreach (Collider2D h in hits)
        {
            if (h == null) continue;
            if (h == selfCol) continue;

            isTouchingWall = true;
            wallJumpDirX = (h.bounds.center.x > transform.position.x) ? -1f : 1f;
            break;
        }
    }

    // ===== GRAVITY / JUMP FEEL =====
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
        if (isRolling) state = MovementState.rolling;
        else if (rb.linearVelocity.y > 0.1f) state = MovementState.jumping;
        else if (rb.linearVelocity.y < -0.1f) state = MovementState.falling;
        else if (Mathf.Abs(horizontal) > EPS) state = MovementState.running;
        else state = MovementState.idle;

        if (anim != null)
            anim.SetInteger("State", (int)state);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck)
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);

        if (wallCheck)
            Gizmos.DrawWireSphere(wallCheck.position, wallCheckRadius);
    }
}
