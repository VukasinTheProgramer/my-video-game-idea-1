using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows "Gold: N", bound to the player's Wallet.OnGoldChanged.</summary>
[RequireComponent(typeof(Text))]
public class GoldHUDUI : MonoBehaviour
{
    private Text label;
    private Wallet wallet;

    private void Awake()
    {
        label = GetComponent<Text>();
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
        label.text = $"Gold: {gold}";
    }
}
