using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows the floor icon + number, bound to GameManager.OnFloorChanged.</summary>
[RequireComponent(typeof(Text))]
public class FloorIndicatorUI : MonoBehaviour
{
    private const string IconResourcePath = "Icons/Floor";
    private const float IconSize = 24f;
    private const float IconGap = 4f;

    private Text label;
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
        HandleFloorChanged(GameManager.Instance.CurrentFloor);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFloorChanged -= HandleFloorChanged;
    }

    private void HandleFloorChanged(int floor)
    {
        label.text = floor.ToString();
    }
}
