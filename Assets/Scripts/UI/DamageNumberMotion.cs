using UnityEngine;

/// <summary>
/// Floats a spawned damage-number GameObject upward while fading it out, then
/// destroys it. Attached at runtime by DamageNumberSpawner - not meant to be
/// added manually in the editor.
/// </summary>
[RequireComponent(typeof(TextMesh))]
public class DamageNumberMotion : MonoBehaviour
{
    private float distance;
    private float lifetime;
    private float elapsed;
    private Vector3 start;
    private TextMesh textMesh;

    public void Init(float floatDistance, float lifetimeSeconds)
    {
        distance = floatDistance;
        lifetime = Mathf.Max(0.01f, lifetimeSeconds);
        start = transform.position;
        textMesh = GetComponent<TextMesh>();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);

        transform.position = start + Vector3.up * (distance * t);

        Color c = textMesh.color;
        c.a = 1f - t;
        textMesh.color = c;

        if (elapsed >= lifetime) Destroy(gameObject);
    }
}
