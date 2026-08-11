using System.Collections;
using UnityEngine;

/// <summary>
/// Drives 4-directional walk/attack sprites for a character with true per-direction
/// art (not flip-based). Optional add-on: Entity looks for this component and, if
/// present, routes facing/attack through it instead of the flipX fallback. Also
/// drives an optional Equipment component so equipped item layers stay in sync
/// with the base body's current frame.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DirectionalSpriteAnimator : MonoBehaviour
{
    [SerializeField] private Sprite[] walkDown;
    [SerializeField] private Sprite[] walkLeft;
    [SerializeField] private Sprite[] walkRight;
    [SerializeField] private Sprite[] walkUp;
    [SerializeField] private Sprite[] attackDown;
    [SerializeField] private Sprite[] attackLeft;
    [SerializeField] private Sprite[] attackRight;
    [SerializeField] private Sprite[] attackUp;
    [SerializeField] private Sprite[] hurt; // not directional in this pack - same frames regardless of facing
    [SerializeField] private float attackFrameSeconds = 0.05f;
    [SerializeField] private float hurtFrameSeconds = 0.05f;

    private SpriteRenderer spriteRenderer;
    private Equipment equipment;
    private Sprite[] currentWalkSet;
    private Coroutine actionRoutine;
    // Which action actionRoutine is running. Walk is the only one a new walk may
    // cut short: stepping tile after tile must restart the stride each time, while
    // an attack or hurt owns the body until it finishes.
    private AnimAction currentAction;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        equipment = GetComponent<Equipment>();
        currentWalkSet = walkDown;
        ShowWalkFrameZero();
        equipment?.ApplyFrame(FacingToDirection(), AnimAction.Walk, 0);
    }

    public void SetFacing(Vector2Int direction)
    {
        currentWalkSet = PickSet(direction, walkDown, walkLeft, walkRight, walkUp);
        if (actionRoutine == null)
        {
            ShowWalkFrameZero();
            equipment?.ApplyFrame(FacingToDirection(), AnimAction.Walk, 0);
        }
    }

    /// <summary>
    /// Resting pose for the current facing. Unity serializes an unassigned Sprite[]
    /// as length 0, so indexing blindly would throw - and when that throw happens
    /// inside RunAction it skips the actionRoutine = null reset, which permanently
    /// wedges SetFacing and freezes the sprite's direction for the rest of the run.
    /// </summary>
    private void ShowWalkFrameZero()
    {
        if (currentWalkSet != null && currentWalkSet.Length > 0 && currentWalkSet[0] != null)
        {
            spriteRenderer.sprite = currentWalkSet[0];
        }
    }

    /// <summary>
    /// Runs the walk cycle once across <paramref name="durationSeconds"/>, so the
    /// stride finishes exactly as Entity's tile-to-tile slide does. Without this
    /// the body only ever showed walk frame 0 and appeared to glide between cells -
    /// the frames were wired on the prefab all along, nothing was cycling them.
    ///
    /// An in-flight attack or hurt wins; only another walk may restart it, so
    /// following a multi-tile path re-triggers the stride per step instead of
    /// being swallowed by the previous step's routine.
    /// </summary>
    public void PlayWalk(float durationSeconds)
    {
        if (actionRoutine != null && currentAction != AnimAction.Walk) return;
        if (currentWalkSet == null || currentWalkSet.Length == 0) return;

        PlayAction(currentWalkSet, currentWalkSet.Length,
            durationSeconds / currentWalkSet.Length, AnimAction.Walk);
    }

    public void PlayAttack()
    {
        Sprite[] frames = PickSet(FacingToDirection(), attackDown, attackLeft, attackRight, attackUp);
        // The bare body has no swing art of its own, but an equipped weapon still
        // needs its slash frames driven - so run the clock regardless, using the
        // standard LPC slash length when the body contributes no frames.
        int frameCount = frames != null && frames.Length > 0 ? frames.Length : LpcSpriteFormat.SlashFrames;
        PlayAction(frames, frameCount, attackFrameSeconds, AnimAction.Attack);
    }

    public void PlayHurt()
    {
        int frameCount = hurt != null && hurt.Length > 0 ? hurt.Length : LpcSpriteFormat.HurtFrames;
        PlayAction(hurt, frameCount, hurtFrameSeconds, AnimAction.Hurt);
    }

    private void PlayAction(Sprite[] frames, int frameCount, float frameSeconds, AnimAction action)
    {
        if (actionRoutine != null) StopCoroutine(actionRoutine);
        currentAction = action;
        actionRoutine = StartCoroutine(RunAction(frames, frameCount, frameSeconds, action));
    }

    private Vector2Int FacingToDirection()
    {
        if (currentWalkSet == walkLeft) return Vector2Int.left;
        if (currentWalkSet == walkRight) return Vector2Int.right;
        if (currentWalkSet == walkUp) return Vector2Int.up;
        return Vector2Int.down;
    }

    private static Sprite[] PickSet(Vector2Int direction, Sprite[] down, Sprite[] left, Sprite[] right, Sprite[] up)
    {
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            return direction.x < 0 ? left : right;
        }
        return direction.y < 0 ? down : up;
    }

    private IEnumerator RunAction(Sprite[] frames, int frameCount, float frameSeconds, AnimAction action)
    {
        Vector2Int direction = FacingToDirection();
        for (int i = 0; i < frameCount; i++)
        {
            // Null-check the frame itself, not just the bounds: a bare base body
            // legitimately has no art for an action, and assigning a null sprite
            // would blank the body mid-swing instead of holding its walk frame.
            if (frames != null && i < frames.Length && frames[i] != null) spriteRenderer.sprite = frames[i];
            equipment?.ApplyFrame(direction, action, i);
            yield return new WaitForSeconds(frameSeconds);
        }
        // Re-read facing rather than reusing the captured direction: SetFacing can
        // run mid-action, and the body and equipment layers must not end up
        // pointing opposite ways.
        ShowWalkFrameZero();
        equipment?.ApplyFrame(FacingToDirection(), AnimAction.Walk, 0);
        actionRoutine = null;
    }
}
