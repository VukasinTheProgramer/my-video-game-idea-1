using UnityEngine;

/// <summary>
/// Spawns floating combat text above entities: damage, crit, MISS, PARRY, and
/// heal numbers (IMPLEMENTED.md -> "Combat feedback"). Self-bootstrapping singleton like
/// TurnManager/GameManager, except Instance creates itself on first use so no
/// manual scene setup is required to get feedback working.
///
/// Entity calls this directly from Attack()/Heal() rather than going through
/// a UI-side event subscription - there's only one listener today, so the
/// extra indirection isn't earning its keep yet. If a second system needs the
/// same hook later, promote this to subscribe to Entity.OnAttackResolved
/// instead.
/// </summary>
public class DamageNumberSpawner : MonoBehaviour
{
    private static DamageNumberSpawner instance;

    public static DamageNumberSpawner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("DamageNumberSpawner");
                instance = go.AddComponent<DamageNumberSpawner>();
            }
            return instance;
        }
    }

    [Header("Optional pixel font (e.g. Press Start 2P) - falls back to Unity's default if unassigned")]
    [SerializeField] private Font font;

    [Header("Motion / lifetime")]
    [SerializeField] private float spawnHeightOffset = 0.9f;
    [SerializeField] private float floatDistance = 0.6f;
    [SerializeField] private float lifetimeSeconds = 0.7f;

    [Header("Size")]
    [SerializeField] private float normalCharacterSize = 0.1f;
    [SerializeField] private float bigCharacterSize = 0.14f;

    // IMPLEMENTED.md -> "Combat feedback": MISS/PARRY/CRIT text+color come from
    // CombatFeedbackText (shared with BattleScreenUI); heal is the one outcome
    // that class doesn't cover, so its color stays local to this spawner.
    private static readonly Color HealColor = new Color(0.3f, 0.9f, 0.35f);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    /// <summary>Shows the outcome of one Entity.Attack call above the defender.</summary>
    public void ShowAttackResult(Entity target, CombatResult result)
    {
        var (text, color, big) = CombatFeedbackText.For(result);
        Spawn(target, text, color, big);
    }

    /// <summary>Shows a heal number (potions, life steal, future abilities) above the healed entity.</summary>
    public void ShowHeal(Entity target, int amount)
    {
        if (amount <= 0) return;
        Spawn(target, "+" + amount, HealColor, big: false);
    }

    private void Spawn(Entity target, string text, Color color, bool big)
    {
        if (target == null) return;
        // Battle screen already shows its own UI-space feedback
        // (BattleScreenUI.ShowCombatFeedback) on top of its full-screen overlay -
        // this world-space spawn would just sit invisible behind it. Suppress it
        // while a battle's active instead of wasting a GameObject every hit.
        if (BattleManager.Instance != null && BattleManager.Instance.IsActive) return;

        var go = new GameObject("DamageNumber");
        go.transform.position = target.transform.position + Vector3.up * spawnHeightOffset;

        TextMesh textMesh = go.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.color = color;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 48;
        textMesh.characterSize = big ? bigCharacterSize : normalCharacterSize;

        MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
        meshRenderer.sortingOrder = 100; // draw above body/equipment sprite layers

        if (font != null)
        {
            textMesh.font = font;
            meshRenderer.material = font.material;
        }

        DamageNumberMotion motion = go.AddComponent<DamageNumberMotion>();
        motion.Init(floatDistance, lifetimeSeconds);
    }
}
