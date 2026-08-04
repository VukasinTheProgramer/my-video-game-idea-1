using UnityEngine;

/// <summary>
/// Weighted drop table an enemy rolls against on death. Each entry's
/// EquippableItem already carries its own fixed rarity/stats, so rolling a
/// table entry rolls both "which item" and "how rare" at once - no separate
/// rarity-roll step. No asset of this type has been authored yet, so no
/// enemy actually rolls one - see IN_PROGRESS.md -> "1b. Enemy loot".
/// </summary>
[CreateAssetMenu(menuName = "Items/Loot Table")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public EquippableItem item;
        [Min(0f)] public float weight = 1f;
    }

    [Tooltip("Chance anything drops at all before the weighted item pick runs.")]
    [Range(0f, 1f)] [SerializeField] private float dropChance = 0.3f;

    [SerializeField] private Entry[] entries;

    /// <summary>Rolls dropChance first, then a weighted pick among entries. Returns null on no-drop or an empty/misconfigured table.</summary>
    public EquippableItem RollDrop()
    {
        if (entries == null || entries.Length == 0) return null;
        if (Random.value > dropChance) return null;

        // Entries with no item, or zero/negative weight, are skipped entirely -
        // otherwise a misconfigured row can consume the roll and return null,
        // which is indistinguishable from "nothing dropped".
        float totalWeight = 0f;
        foreach (Entry entry in entries)
        {
            if (entry?.item == null || entry.weight <= 0f) continue;
            totalWeight += entry.weight;
        }
        if (totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        foreach (Entry entry in entries)
        {
            if (entry?.item == null || entry.weight <= 0f) continue;

            cumulative += entry.weight;
            // Strict < so an exact Random.value of 0 can't select the first entry
            // before any weight has accumulated.
            if (roll < cumulative) return entry.item;
        }

        return null;
    }
}
