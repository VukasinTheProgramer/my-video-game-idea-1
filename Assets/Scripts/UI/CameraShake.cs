using System.Collections;
using UnityEngine;

/// <summary>
/// Screen shake on crits (COMBAT_DESIGN.md §6). Self-bootstrapping singleton
/// like DamageNumberSpawner, except CurrentOffset is a passive static read that
/// does NOT force-create the instance - only Shake() does - so a game where
/// nothing ever crits never spawns the object.
///
/// Deliberately does not touch the Main Camera's transform itself: CameraFollow
/// already writes transform.position every LateUpdate, and Unity doesn't
/// guarantee ordering between two different components' LateUpdate calls.
/// Instead this just decays a Vector3 offset over time; CameraFollow adds it
/// after computing its own base position, so there's exactly one writer of the
/// camera's transform and no ordering hazard. Once the Pixel Perfect Camera
/// (§6a) is added, keep this the same way - shaking the camera's own transform
/// directly would fight pixel snapping.
/// </summary>
public class CameraShake : MonoBehaviour
{
    private static CameraShake instance;

    /// <summary>Current per-frame shake offset in world units. Vector3.zero if nothing is shaking (and doesn't create the singleton just by being read).</summary>
    public static Vector3 CurrentOffset => instance != null ? instance.currentOffset : Vector3.zero;

    /// <summary>
    /// Starts (or restarts, if already shaking) a decaying shake.
    /// amplitude is in world units, not pixels - GridUtils.CellSize is 1 world
    /// unit per tile, so e.g. 0.05 is a subtle shake at 5% of a tile. Once §6a's
    /// Pixel Perfect Camera fixes a project-wide Pixels Per Unit, convert a
    /// pixel amount to world units as pixels / PPU before calling this.
    /// </summary>
    public static void Shake(float amplitude, float durationSeconds)
    {
        if (instance == null)
        {
            var go = new GameObject("CameraShake");
            instance = go.AddComponent<CameraShake>();
        }
        instance.StartShake(amplitude, durationSeconds);
    }

    private Vector3 currentOffset;
    private Coroutine shakeRoutine;

    private void StartShake(float amplitude, float durationSeconds)
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine(amplitude, durationSeconds));
    }

    private IEnumerator ShakeRoutine(float amplitude, float durationSeconds)
    {
        float t = 0f;
        while (t < durationSeconds)
        {
            t += Time.deltaTime;
            float remaining = 1f - t / durationSeconds; // shrinks 1 -> 0 over the shake's lifetime
            currentOffset = Random.insideUnitCircle * amplitude * remaining;
            yield return null;
        }
        currentOffset = Vector3.zero;
        shakeRoutine = null;
    }
}
