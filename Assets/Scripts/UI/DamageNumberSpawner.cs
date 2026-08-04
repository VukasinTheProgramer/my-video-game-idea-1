using UnityEngine;

/// <summary>
/// Spawns floating combat text above entities: damage, crit, MISS, PARRY, and
/// heal numbers (COMBAT_DESIGN.md §6). Self-bootstrapping singleton like
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

    // COMBAT_DESIGN.md §6 damage-number colors.
    private static readonly Color MissColor = new Color(0.75f, 0.75f, 0.75f);
    private static readonly Color ParryColor = new Color(0.35f, 0.55f, 1f);
    private static readonly Color CritColor = Color.yellow;
    private static readonly Color NormalColor = Color.white;
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
        if (result.WasDodged)
        {
            Spawn(target, "MISS", MissColor, big: false);
            return;
        }

        if (result.WasParried)
        {
            Spawn(target, "PARRY", ParryColor, big: false);
            return;
        }

        Spawn(target, result.Damage.ToString(), result.WasCrit ? CritColor : NormalColor, big: result.WasCrit);
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
