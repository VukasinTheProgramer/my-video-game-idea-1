using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A solid fixture the player can walk up to and act on with an explicit E press
/// (PlayerController.FindNearbyInteractable/Update) - the ambient "Read (E)"/
/// "Open (E)" prompt shown at InteractRange never fires the action itself, only
/// Interact() does. Implementations register EVERY cell they occupy into
/// DungeonGrid's interactable registry (all pointing at the same instance) and
/// mark each of those cells solid on Start (see SignPost/LootChest) - a 1-tile
/// fixture's Cells is just a single-element array.
/// </summary>
public interface IInteractable
{
    /// <summary>Every cell this fixture occupies (at least one). Implementations
    /// should cache this array once (e.g. in Start) rather than allocate on each
    /// access - PlayerController.FindNearbyInteractable reads it every frame.</summary>
    IReadOnlyList<Vector2Int> Cells { get; }

    /// <summary>Manhattan-distance range at which the ambient prompt appears,
    /// measured from the NEAREST of this fixture's Cells - see
    /// GridUtils.WithinRange. Deliberately Manhattan, not Chebyshev, for every
    /// interactable so far (see SignPost.InteractRange's own tooltip for why).</summary>
    int InteractRange { get; }

    /// <summary>Verb shown in the prompt, e.g. "Read" or "Open" - rendered as
    /// "{PromptLabel} (E)" by InteractionPromptUI.</summary>
    string PromptLabel { get; }

    void Interact(PlayerController player);
}
