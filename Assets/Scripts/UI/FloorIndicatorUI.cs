using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows "Floor N", bound to GameManager.OnFloorChanged.</summary>
[RequireComponent(typeof(Text))]
public class FloorIndicatorUI : MonoBehaviour
{
    private Text label;

    private void Awake()
    {
        label = GetComponent<Text>();
    }

    private void Start()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnFloorChanged += HandleFloorChanged;
        HandleFloorChanged(GameManager.Instance.CurrentFloor);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFloorChanged -= HandleFloorChanged;
    }

    private void HandleFloorChanged(int floor)
    {
        label.text = $"Floor {floor}";
    }
}
