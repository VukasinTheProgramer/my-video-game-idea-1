using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// First thing the player sees: full-screen "THE DUNGEON" title with Start/Quit,
/// blocking gameplay until Start is pressed. Self-bootstrapping singleton like the
/// outro screens (VictoryScreenUI/LevelUpUI/FloorCompleteUI), but forces its own
/// creation via RuntimeInitializeOnLoadMethod instead of waiting for a gameplay
/// event to call Instance - nothing gameplay-side ever needs to show THIS screen,
/// it has to already be up before gameplay starts. GameManager no longer generates
/// the first floor from its own Start() - the Start button calls
/// GameManager.BeginRun() instead, so the dungeon genuinely doesn't exist until
/// the player presses Start, not just visually covered by a menu.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    private static MainMenuUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        _ = Instance; // touching Instance forces creation, which shows itself in Awake
    }

    private static MainMenuUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("MainMenuUI");
                instance = go.AddComponent<MainMenuUI>();
            }
            return instance;
        }
    }

    private RectTransform root;

    private void Awake()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("MainMenuUI: no Canvas found in the scene - this screen can never render.");
            return;
        }

        var rootGO = new GameObject("MainMenuScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root = (RectTransform)rootGO.transform;
        root.SetParent(canvas.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        rootGO.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f);

        BuildTitle(root);
        BuildButtonPanel(root);

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        root.SetAsLastSibling();
    }

    private static void BuildTitle(Transform parent)
    {
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(Text));
        var titleRt = (RectTransform)titleGO.transform;
        titleRt.SetParent(parent, false);
        titleRt.anchorMin = new Vector2(0.5f, 0.65f);
        titleRt.anchorMax = new Vector2(0.5f, 0.65f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(900f, 140f);

        Text title = titleGO.GetComponent<Text>();
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.fontSize = 72;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.text = "THE DUNGEON";
    }

    private void BuildButtonPanel(Transform parent)
    {
        var panelGO = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var panelRt = (RectTransform)panelGO.transform;
        panelRt.SetParent(parent, false);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.4f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(260f, 0f);

        var layout = panelGO.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
        panelGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MakeButton(panelRt, "Start", HandleStartClicked);
        MakeButton(panelRt, "Quit", HandleQuitClicked);
    }

    private static void MakeButton(Transform parent, string label, UnityAction onClick)
    {
        var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonGO.transform.SetParent(parent, false);
        buttonGO.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
        buttonGO.GetComponent<LayoutElement>().preferredHeight = 44f;
        buttonGO.GetComponent<Button>().onClick.AddListener(onClick);

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRt = labelGO.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        Text text = labelGO.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
    }

    private static void HandleStartClicked()
    {
        if (Instance.root != null) Instance.root.gameObject.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.BeginRun();
    }

    private static void HandleQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
