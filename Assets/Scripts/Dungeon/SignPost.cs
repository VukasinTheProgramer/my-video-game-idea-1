using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A readable IInteractable fixture occupying one dungeon cell. Registers into
/// DungeonGrid's interactable registry and marks its own cell solid on Start: a
/// sign is a prop, not floor, so the player can never stand exactly on it. Still a
/// valid click target despite that (Pathfinder.Search lets the goal cell bypass
/// both the walkable and occupied checks) - clicking it walks the player as close
/// as InteractRange allows and stops there, same as an enemy is approached but
/// never actually walked into.
///
/// Prefab-based like HealthPotionPickup: assign the sign sprite/message/range in
/// the Inspector, no code-side sprite loading. Not an ItemPickup - a sign is
/// never consumed/removed, so it doesn't share that base's PickUp contract.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SignPost : MonoBehaviour, IInteractable
{
    [TextArea]
    [SerializeField] private string message = "A weathered sign.";

    [Tooltip("Manhattan-distance range at which walking near this sign shows its \"Read (E)\" prompt - only the 4 orthogonal neighbors count at range 1, not the 4 diagonals (unlike BattleManager.EngageRange's Chebyshev convention - an interactable's prompt zone is deliberately the smaller, straight-line-only shape).")]
    [Min(1)] [SerializeField] private int readRange = 1;

    public string Message => message;
    public Vector2Int Cell { get; private set; }
    public IReadOnlyList<Vector2Int> Cells { get; private set; }
    public int InteractRange => readRange;
    public string PromptLabel => "Read";

    /// <summary>Overrides this instance's message at spawn time - lets one shared
    /// prefab carry different text per placement (e.g. GameManager's floor-1 "how
    /// to move" sign vs. its floor-2 "how to open chests" one) without needing a
    /// separate prefab per message, same code-settable-field spirit as
    /// EnemyController.SetGoldFloorBonus.</summary>
    public void SetMessage(string text) => message = text;

    private void Start()
    {
        Cell = GridUtils.WorldToCell(transform.position);
        Cells = new[] { Cell }; // cached once - always exactly one cell for a sign
        DungeonGrid.RegisterInteractable(Cell, this);
        DungeonGrid.RemoveWalkable(Cell);
    }

    public void Interact(PlayerController player)
    {
        MessagePopupUI.Instance.Show("Sign", message);
    }
}
