using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A one-time IInteractable fixture: rolls its LootTable and grants every result
/// straight to the interacting player's Inventory (same pattern as
/// EnemyController.Die() - floorStep 0, see that method's own comment for why),
/// shows a summary via MessagePopupUI (auto-dismissing after 5s - opening a chest
/// shouldn't require a button press to get back to playing), then unregisters
/// every cell it occupies, reopens them to walking, and destroys itself. Unlike
/// SignPost, a chest is single-use and disappears once opened - nothing is left
/// behind to walk around.
///
/// Footprint (small 1x1 vs large 2x1, or any other size) is data, not a
/// subclass - two prefabs sharing this same script with different footprint
/// values, matching CLAUDE.md rule 4 ("Data that designers tune = ScriptableObject/
/// serialized field. Logic = component. Never both in one class."). Origin is the
/// footprint's bottom-left cell; it extends footprint.x cells right and
/// footprint.y cells up from there, matching how DungeonGenerator.CarveRoom
/// already reads a RectInt's width/height.
///
/// Prefab-based like HealthPotionPickup/SignPost: assign the sprite/lootTable/
/// footprint in the Inspector, no code-side sprite loading.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LootChest : MonoBehaviour, IInteractable
{
    [SerializeField] private LootTable lootTable;

    [Tooltip("Multiplies every LootTable slot's roll chance (LootTable.RollDrops) - a chest should roll loot noticeably more often than a plain enemy kill. Small chest default: +50% (x1.5). Large chest is set to x2 (+100%) on its own prefab.")]
    [SerializeField] private float lootChanceMultiplier = 1.5f;

    [Tooltip("Multiplies the combined Set/Legendary/Mythic rarity chance (LootTable.RollDrops) - 1 = no boost, same odds as a plain enemy kill. Only the large chest's prefab raises this (x3, +200%); the small chest leaves it at 1.")]
    [SerializeField] private float rarityBoostMultiplier = 1f;

    [Tooltip("Flat gold range granted alongside item loot, same hand-tuned-range spirit as EnemyController.goldRewardMin/Max.")]
    [SerializeField] private int goldRewardMin = 5;
    [SerializeField] private int goldRewardMax = 15;

    [Tooltip("If true, this chest's loot always includes at least one item of guaranteedMinRarity or better (LootTable.RollAtLeastRarity), topped up after the normal roll if it didn't already produce one - a special/rare chest variant, not the default for a plain chest.")]
    [SerializeField] private bool guaranteesMinRarity = false;
    [SerializeField] private Rarity guaranteedMinRarity = Rarity.Rare;

    [Tooltip("How many cells this chest occupies, right and up from its placed position - (1,1) for a small chest, (2,1) for a wider large one. GameManager.SpawnChests reads this off the PREFAB (no instantiation needed) to validate every cell in the footprint before placing.")]
    [SerializeField] private Vector2Int footprint = Vector2Int.one;

    [Tooltip("Manhattan-distance range at which walking near ANY of this chest's cells shows its \"Open (E)\" prompt - see SignPost.InteractRange's tooltip for why Manhattan, not Chebyshev.")]
    [Min(1)] [SerializeField] private int interactRange = 1;

    public Vector2Int Footprint => footprint;
    public IReadOnlyList<Vector2Int> Cells { get; private set; }
    public int InteractRange => interactRange;
    public string PromptLabel => "Open";

    private void Start()
    {
        Vector2Int origin = GridUtils.WorldToCell(transform.position);

        var cells = new List<Vector2Int>(footprint.x * footprint.y);
        for (int x = 0; x < footprint.x; x++)
        {
            for (int y = 0; y < footprint.y; y++)
            {
                cells.Add(origin + new Vector2Int(x, y));
            }
        }
        Cells = cells; // cached once - FindNearbyInteractable reads this every frame

        foreach (Vector2Int cell in Cells)
        {
            DungeonGrid.RegisterInteractable(cell, this);
            DungeonGrid.RemoveWalkable(cell);
        }
    }

    public void Interact(PlayerController player)
    {
        // Unregistered and reopened to walking first, not last: if anything below
        // throws, the chest still stops handing out a second helping and doesn't
        // leave a permanently-solid tile behind.
        foreach (Vector2Int cell in Cells)
        {
            DungeonGrid.UnregisterInteractable(cell);
            DungeonGrid.AddWalkable(cell);
        }

        if (player != null)
        {
            List<EquippableItem> granted = new List<EquippableItem>();
            if (lootTable != null)
            {
                Inventory inventory = player.GetComponent<Inventory>();
                var drops = lootTable.RollDropsWithGuarantee(
                    lootChanceMultiplier, rarityBoostMultiplier, guaranteesMinRarity, guaranteedMinRarity);
                foreach (EquippableItem drop in drops)
                {
                    // floorStep 0 - same "no item-level scaling yet" reasoning as
                    // EnemyController.Die() (IMPLEMENTED.md -> "Item generation").
                    EquippableItem generated = ItemGenerator.Generate(drop, 0, drop.rarity);
                    inventory?.Add(generated);
                    granted.Add(generated);
                }
            }

            Wallet wallet = player.GetComponent<Wallet>();
            int gold = Random.Range(goldRewardMin, goldRewardMax + 1);
            wallet?.AddGold(gold);

            MessagePopupUI.Instance.Show("Chest", BuildSummary(granted, gold), autoDismissSeconds: 5f);
        }

        Destroy(gameObject);
    }

    private static string BuildSummary(List<EquippableItem> granted, int gold)
    {
        var sb = new StringBuilder();
        if (granted.Count == 0)
        {
            sb.Append("The chest held only gold.\n");
        }
        else
        {
            sb.Append("You found:\n");
            foreach (EquippableItem item in granted)
            {
                sb.Append("- ").Append(item.displayName).Append('\n');
            }
        }
        sb.Append("+").Append(gold).Append(" gold");
        return sb.ToString();
    }
}
