using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator anim;
    private Collider2D selfCol;

    private enum MovementState { idle, running, jumping, falling, dashing }
    private MovementState state;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 50f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private int maxJumps = 2;

    [Header("Jump Buffer (Grace Timer)")]
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
    [SerializeField] private Vector2 wallCheckOffset = new Vector2(0.35f, 0.0f); // flips sides with facing
    [SerializeField] private float wallCheckRadius = 0.14f;
    [SerializeField] private LayerMask wallLayer;

    [Header("Wall Slide")]
    [SerializeField] private float wallSlideMaxFallSpeed = -4f;
    [SerializeField] private float wallSlideGravityMultiplier = 0.5f;
    [SerializeField] private bool requireHoldingTowardWallToSlide = true;

    [Header("Wall Jump")]
    [SerializeField] private float wallJumpForce = 12f;
    [SerializeField] private float wallJumpHorizontalForce = 8f;
    [SerializeField] private float wallJumpLockTime = 0.15f;
    [SerializeField] private int refillJumpsAfterWallJump = 1; // set to maxJumps to fully refill

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

    // Jump buffer + coyote
    private float jumpBufferLeft = 0f;
    private float coyoteTimeLeft = 0f;

    // Wall state
    private bool isTouchingWall;
    private bool isWallSliding;
    private float wallJumpDirX; // -1 jump left, +1 jump right

    // Wall jump lock
    private float wallJumpLockLeft = 0f;

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
        // Input
        horizontal =
            (Keyboard.current.rightArrowKey.isPressed ? 1f : 0f) +
            (Keyboard.current.leftArrowKey.isPressed ? -1f : 0f);

        downHeld = Keyboard.current.downArrowKey.isPressed;

        // Jump buffer
        if (Keyboard.current.zKey.wasPressedThisFrame)
            jumpBufferLeft = jumpBufferTime;

        if (jumpBufferLeft > 0f)
            jumpBufferLeft -= Time.deltaTime;

        // Ground check + coyote
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (isGrounded)
        {
            jumpsRemaining = maxJumps;
            coyoteTimeLeft = coyoteTime;
            if (refreshDashOnGround) dashCooldownLeft = 0f;
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

        // Keep wallCheck on the facing side
        UpdateWallCheckPosition();

        // Wall detect + slide state
        ResolveWallContact();

        bool holdingTowardWall = Mathf.Abs(horizontal) > EPS && Mathf.Sign(horizontal) == Mathf.Sign(-wallJumpDirX);
        isWallSliding =
            !isGrounded &&
            !isDashing &&
            isTouchingWall &&
            rb.linearVelocity.y <= 0f &&
            (!requireHoldingTowardWallToSlide || holdingTowardWall);

        // Dash start
        if (Keyboard.current.cKey.wasPressedThisFrame && !isDashing && dashCooldownLeft <= 0f)
            StartDash(downHeld);

        // Cancel dash if player turns around
        if (isDashing && cancelDashOnTurn && Mathf.Abs(horizontal) > EPS)
        {
            if (Mathf.Sign(horizontal) != dashDirSign)
                EndDash();
        }

        // Consume buffered jump (wall jump priority)
        TryConsumeBufferedJump();

        // Variable jump cut
        if (Keyboard.current.zKey.wasReleasedThisFrame && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier
            );
        }

        UpdateAnimationState();
    }

    void FixedUpdate()
    {
        if (dashCooldownLeft > 0f)
            dashCooldownLeft -= Time.fixedDeltaTime;

        if (wallJumpLockLeft > 0f)
            wallJumpLockLeft -= Time.fixedDeltaTime;

        if (isDashing)
        {
            DashTick();
            return;
        }

        // Horizontal ramping (ignore input briefly after wall jump)
        float inputX = (wallJumpLockLeft > 0f) ? 0f : horizontal;

        float targetX = inputX * moveSpeed;
        float rate = Mathf.Abs(targetX) > EPS ? acceleration : deceleration;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

        // Wall slide override (must win over better jump physics)
        if (isWallSliding)
        {
            rb.gravityScale = baseGravity * wallSlideGravityMultiplier;

            if (rb.linearVelocity.y < wallSlideMaxFallSpeed)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, wallSlideMaxFallSpeed);

            return;
        }

        ApplyBetterJumpPhysics();
    }

    private void UpdateWallCheckPosition()
    {
        if (wallCheck == null) return;

        float side = sr.flipX ? -1f : 1f; // facing left if flipped
        wallCheck.position = (Vector2)transform.position + new Vector2(wallCheckOffset.x * side, wallCheckOffset.y);
    }

    private void ResolveWallContact()
    {
        isTouchingWall = false;
        wallJumpDirX = 0f;

        if (isGrounded || isDashing || wallCheck == null) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(wallCheck.position, wallCheckRadius, wallLayer);

        Collider2D best = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D h = hits[i];
            if (h == null) continue;
            if (h == selfCol) continue;
            if (h.attachedRigidbody != null && h.attachedRigidbody == rb) continue;

            best = h;
            break;
        }

        if (best != null)
        {
            isTouchingWall = true;
            wallJumpDirX = (best.bounds.center.x > transform.position.x) ? -1f : 1f;
        }
    }

    private void TryConsumeBufferedJump()
    {
        if (jumpBufferLeft <= 0f) return;
        if (isDashing) return;

        // WALL JUMP (allowed even if you used all double jumps)
        if (!isGrounded && isTouchingWall && wallJumpDirX != 0f)
        {
            rb.linearVelocity = new Vector2(wallJumpDirX * wallJumpHorizontalForce, wallJumpForce);

            // Face jump direction
            sr.flipX = wallJumpDirX < 0f;

            wallJumpLockLeft = wallJumpLockTime;

            // Refill air jumps after wall jump
            jumpsRemaining = Mathf.Clamp(refillJumpsAfterWallJump, 0, maxJumps);

            // Consume buffer + stop coyote
            jumpBufferLeft = 0f;
            coyoteTimeLeft = 0f;

            return;
        }

        // NORMAL JUMP (ground/coyote/air)
        bool canUseCoyote = (coyoteTimeLeft > 0f) && (jumpsRemaining == maxJumps);

        if (jumpsRemaining > 0 || canUseCoyote)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);

            if (!canUseCoyote)
                jumpsRemaining--;

            jumpBufferLeft = 0f;
            coyoteTimeLeft = 0f;
        }
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
        if (groundCheck != null)
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);

        if (wallCheck != null)
            Gizmos.DrawWireSphere(wallCheck.position, wallCheckRadius);
    }
}
