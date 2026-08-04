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

    // Input.GetKeyDown is true for the whole frame across every script, and this
    // screen is chained straight off VictoryScreenUI's Continue - so one Space
    // press would confirm Victory and then immediately confirm this too, in the
    // same frame. See VictoryScreenUI.shownOnFrame for the full explanation.
    private int shownOnFrame = -1;

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("LevelUpScreen", out titleText, out bodyText, out continueButton);
        if (root == null) return;

        titleText.text = "Level Up!";
        continueButton.onClick.AddListener(HandleContinueClicked);
        root.gameObject.SetActive(false);
    }

    /// <summary>False on the frame this was shown, so the keypress that opened it
    /// can't also confirm it. See shownOnFrame.</summary>
    private bool CanAcceptInput() =>
        root != null && root.gameObject.activeSelf && Time.frameCount != shownOnFrame;

    private void Update()
    {
        if (!CanAcceptInput()) return;
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
        shownOnFrame = Time.frameCount;
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
