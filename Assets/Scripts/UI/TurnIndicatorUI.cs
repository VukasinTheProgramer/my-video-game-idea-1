using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows "Your Turn" / "Enemy Turn", bound to TurnManager.OnTurnStateChanged.</summary>
[RequireComponent(typeof(Text))]
public class TurnIndicatorUI : MonoBehaviour
{
    private Text label;

    private void Awake()
    {
        label = GetComponent<Text>();
    }

    private void Start()
    {
        if (TurnManager.Instance == null) return;
        TurnManager.Instance.OnTurnStateChanged += HandleTurnStateChanged;
        HandleTurnStateChanged(TurnManager.Instance.State);
    }

    private void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnStateChanged -= HandleTurnStateChanged;
    }

    private void HandleTurnStateChanged(TurnState state)
    {
        label.text = state == TurnState.PlayerTurn ? "Your Turn" : "Enemy Turn";
    }
}
