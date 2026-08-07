using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI-space equivalent of DamageNumberMotion: floats a spawned Text upward
/// (via anchoredPosition, not world position) while fading it out, then
/// destroys itself. Attached at runtime by BattleScreenUI's combat-feedback
/// spawn - not meant to be added manually in the editor.
/// </summary>
[RequireComponent(typeof(Text))]
public class BattleFloatingTextMotion : MonoBehaviour
{
    private float distance;
    private float lifetime;
    private float elapsed;
    private Vector2 start;
    private RectTransform rect;
    private Text text;

    public void Init(float floatDistance, float lifetimeSeconds)
    {
        distance = floatDistance;
        lifetime = Mathf.Max(0.01f, lifetimeSeconds);
        rect = (RectTransform)transform;
        start = rect.anchoredPosition;
        text = GetComponent<Text>();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);

        rect.anchoredPosition = start + Vector2.up * (distance * t);

        Color c = text.color;
        c.a = 1f - t;
        text.color = c;

        if (elapsed >= lifetime) Destroy(gameObject);
    }
}
