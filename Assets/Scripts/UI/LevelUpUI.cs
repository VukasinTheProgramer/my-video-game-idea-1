using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen "you leveled up" screen. Self-bootstrapping singleton, shown
/// by BattleManager right after the victory screen closes - only when that
/// kill's XP crossed a level threshold. PlayerProgression.AddXP can fire
/// OnLevelUp more than once off a single big XP grant; BattleManager keeps
/// only the final level reached, so this only ever shows once per kill.
/// </summary>
public class LevelUpUI : MonoBehaviour
{
    private static LevelUpUI instance;

    public static LevelUpUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("LevelUpUI");
                instance = go.AddComponent<LevelUpUI>();
            }
            return instance;
        }
    }

    private RectTransform root;
    private Text titleText;
    private Text bodyText;
    private Button continueButton;
    private Action pendingContinue;

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("LevelUpScreen", out titleText, out bodyText, out continueButton);
        if (root == null) return;

        titleText.text = "Level Up!";
        continueButton.onClick.AddListener(HandleContinueClicked);
        root.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (root == null || !root.gameObject.activeSelf) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            HandleContinueClicked();
        }
    }

    public void Show(int newLevel, Action onContinue)
    {
        if (root == null)
        {
            onContinue?.Invoke();
            return;
        }

        pendingContinue = onContinue;
        bodyText.text = $"You reached Level {newLevel}!\n\nOpen the character panel to spend your new stat point.";

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        // Built once in Awake and never moved since - re-raise on every show so a
        // later-added Canvas child (equipment panel, VictoryScreenUI's own root,
        // etc) can't end up painting over this. Same fix ItemTooltipUI uses.
        root.transform.SetAsLastSibling();
        root.gameObject.SetActive(true);
    }

    private void HandleContinueClicked()
    {
        root.gameObject.SetActive(false);
        Action callback = pendingContinue;
        pendingContinue = null;
        callback?.Invoke();
    }
}
