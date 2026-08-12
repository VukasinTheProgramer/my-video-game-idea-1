using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows the gold icon + count, bound to the player's Wallet.OnGoldChanged.</summary>
[RequireComponent(typeof(Text))]
public class GoldHUDUI : MonoBehaviour
{
    private const string IconResourcePath = "Icons/Gold";
    private const float IconSize = 24f;
    private const float IconGap = 4f;

    private Text label;
    private Wallet wallet;
    private GameObject icon;

    private void Awake()
    {
        label = GetComponent<Text>();
        icon = HudIcon.AddBeside(GetComponent<RectTransform>(), IconResourcePath, IconSize, IconGap);
    }

    /// <summary>Hides/shows this label and its icon together - the icon is a runtime-spawned
    /// sibling (HudIcon.AddBeside), not a child, so SetActive on this GameObject alone
    /// wouldn't hide it (BattleScreenUI hides HUD elements while its full-screen overlay is up).</summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (icon != null) icon.SetActive(visible);
    }

    private void Start()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnFloorChanged += HandleFloorChanged;
        TrySubscribeWallet();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnFloorChanged -= HandleFloorChanged;
        if (wallet != null) wallet.OnGoldChanged -= HandleGoldChanged;
    }

    // The player is created once by GameManager and only ever moved between
    // floors, so this only needs to succeed once - but OnFloorChanged always
    // fires after the player exists, so it's a safe retry point if Start()
    // ran before GameManager finished its first floor.
    private void HandleFloorChanged(int floor) => TrySubscribeWallet();

    private void TrySubscribeWallet()
    {
        if (wallet != null) return;

        PlayerController player = GameManager.Instance.Player;
        if (player == null) return;

        wallet = player.GetComponent<Wallet>();
        if (wallet == null) return;

        wallet.OnGoldChanged += HandleGoldChanged;
        HandleGoldChanged(wallet.Gold);
    }

    private void HandleGoldChanged(int gold)
    {
        label.text = gold.ToString();
    }
}
