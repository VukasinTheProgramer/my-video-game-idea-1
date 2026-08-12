using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared helper for HUD labels that want a small icon to their left
/// (Gold/Floor). Builds the icon as a sibling positioned where the label
/// currently sits, then shifts the label right to make room - works
/// regardless of where the label was hand-placed in the scene, so no scene
/// wiring is needed beyond the label itself already being there.</summary>
public static class HudIcon
{
    public static GameObject AddBeside(RectTransform label, string resourcePath, float size, float gap)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null)
        {
            Debug.LogError($"HudIcon: no sprite at Resources/{resourcePath} - icon will render empty.");
        }

        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var iconRect = (RectTransform)iconGO.transform;
        iconRect.SetParent(label.parent, false);
        iconRect.anchorMin = label.anchorMin;
        iconRect.anchorMax = label.anchorMax;
        iconRect.pivot = label.pivot;
        iconRect.anchoredPosition = label.anchoredPosition;
        iconRect.sizeDelta = new Vector2(size, size);

        var image = iconGO.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        label.anchoredPosition += new Vector2(size + gap, 0f);
        return iconGO;
    }
}
