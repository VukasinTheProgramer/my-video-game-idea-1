using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small "{Verb} (E)" hint shown near the bottom of the screen while the player is
/// within an IInteractable's InteractRange - purely a passive prompt, not a modal
/// (contrast MessagePopupUI, which owns the actual full-screen message). Self-
/// bootstrapping singleton, same idiom as the other UI classes here, but far
/// simpler: one Text, no backdrop/panel, no input handling of its own -
/// PlayerController drives visibility and text every frame via SetVisible/Hide
/// and reads E itself.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    private static InteractionPromptUI instance;

    public static InteractionPromptUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("InteractionPromptUI");
                instance = go.AddComponent<InteractionPromptUI>();
            }
            return instance;
        }
    }

    private Text label;
    private string currentVerb;

    private void Awake()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("InteractionPromptUI: no Canvas found in the scene - this prompt can never render.");
            return;
        }

        var go = new GameObject("InteractionPrompt", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.12f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 40f);

        label = go.GetComponent<Text>();
        label.font = UIFonts.Default;
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        // Purely decorative - must not swallow map clicks (CLAUDE.md landmine:
        // Text/Image default raycastTarget=true regardless of visible content).
        label.raycastTarget = false;

        go.SetActive(false);
    }

    /// <summary>Shows "{verb} (E)", e.g. "Read (E)" or "Open (E)". Only rewrites the
    /// text when the verb actually changed, not every call - PlayerController calls
    /// this every frame something's in range.</summary>
    public void Show(string verb)
    {
        if (label == null) return;
        if (currentVerb != verb)
        {
            currentVerb = verb;
            label.text = $"{verb} (E)";
        }
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (label == null) return;
        if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
    }
}
