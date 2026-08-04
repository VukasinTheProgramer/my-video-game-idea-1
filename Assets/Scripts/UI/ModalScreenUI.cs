using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared builder for full-screen modal overlays (Victory, Level Up, Floor
/// Complete): dim background + centered panel + title/body text + a
/// Continue button. Self-building per CLAUDE.md's UI convention - each
/// caller is a self-bootstrapping singleton (like DamageNumberSpawner/
/// CameraShake) that parents its overlay under the scene's one Canvas the
/// first time it's shown, so none of the three need any scene wiring.
/// </summary>
internal static class ModalScreenUI
{
    public static RectTransform BuildOverlay(string name, out Text titleText, out Text bodyText, out Button continueButton)
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError($"{name}: no Canvas found in the scene - this screen can never render.");
            titleText = null;
            bodyText = null;
            continueButton = null;
            return null;
        }

        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rootRt = (RectTransform)root.transform;
        rootRt.SetParent(canvas.transform, false);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var panelRt = (RectTransform)panel.transform;
        panelRt.SetParent(rootRt, false);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(360f, 0f);
        panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.97f);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = MakeText(panelRt, "Title", 24, FontStyle.Bold, TextAnchor.MiddleCenter);
        bodyText = MakeText(panelRt, "Body", 16, FontStyle.Normal, TextAnchor.UpperLeft);

        var buttonGO = new GameObject("ContinueButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonGO.transform.SetParent(panelRt, false);
        buttonGO.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
        buttonGO.GetComponent<LayoutElement>().preferredHeight = 34f;
        continueButton = buttonGO.GetComponent<Button>();

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRt = labelGO.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        Text label = labelGO.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "Continue";

        return rootRt;
    }

    private static Text MakeText(Transform parent, string name, int fontSize, FontStyle style, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
