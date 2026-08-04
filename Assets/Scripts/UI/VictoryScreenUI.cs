using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen "you won this fight" summary: damage dealt/taken, XP earned,
/// gold earned, items dropped. Self-bootstrapping singleton (like
/// DamageNumberSpawner/CameraShake) - BattleManager calls Show right after
/// hiding the fight overlay, and only resumes the floor once Continue is
/// pressed, via the onContinue callback.
/// </summary>
public class VictoryScreenUI : MonoBehaviour
{
    private static VictoryScreenUI instance;

    public static VictoryScreenUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("VictoryScreenUI");
                instance = go.AddComponent<VictoryScreenUI>();
            }
            return instance;
        }
    }

    private RectTransform root;
    private Text titleText;
    private Text bodyText;
    private Button continueButton;
    private Action pendingContinue;

    // Input.GetKeyDown stays true for the WHOLE frame, for every script that polls
    // it - so the same Space press that landed the killing blow (read by
    // BattleScreenUI.Update) would also be read by this screen's Update later in
    // that same frame and dismiss it instantly. That's why this only ever appeared
    // on the very first kill: Instance builds the GameObject mid-frame during the
    // first HandleVictory, and a MonoBehaviour created mid-frame gets no Update
    // until the next one. Every later fight reused the existing object and got
    // eaten immediately.
    private int shownOnFrame = -1;

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("VictoryScreen", out titleText, out bodyText, out continueButton);
        if (root == null) return; // no Canvas in the scene - ModalScreenUI already logged it

        titleText.text = "Victory!";
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

    public void Show(BattleResultSummary summary, Action onContinue)
    {
        // Can't render without a Canvas - call onContinue immediately rather than
        // soft-locking BattleManager forever waiting on a screen that can't appear.
        if (root == null)
        {
            onContinue?.Invoke();
            return;
        }

        pendingContinue = onContinue;
        bodyText.text = BuildBody(summary);

        // Space/Return is also the EventSystem's Submit binding - clear any
        // leftover selection (the fight's own Attack button) so the first Space
        // press here means only "continue" (same reasoning as BattleScreenUI.Show).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        // Built once in Awake and never moved since - without this, anything added
        // to the Canvas after that first build (equipment panel, its item tooltip,
        // even LevelUpUI/FloorCompleteUI on their own first use) ends up a later
        // sibling and paints over this on every show after the first. Same fix
        // ItemTooltipUI already uses for the same reason.
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

    private static string BuildBody(BattleResultSummary summary)
    {
        var sb = new StringBuilder();
        sb.Append($"Damage Dealt: {summary.DamageDealt}\n");
        sb.Append($"Damage Taken: {summary.DamageTaken}\n");
        sb.Append($"XP Earned: {summary.XPEarned}\n");
        sb.Append($"Gold Earned: {summary.GoldEarned}\n");

        if (summary.ItemsDropped == null || summary.ItemsDropped.Count == 0)
        {
            sb.Append("Items Dropped: none");
        }
        else
        {
            sb.Append("Items Dropped:\n");
            foreach (EquippableItem item in summary.ItemsDropped)
            {
                if (item == null) continue;
                string hex = ColorUtility.ToHtmlStringRGB(RarityVisuals.OutlineColor(item.rarity));
                sb.Append($"  <color=#{hex}>{item.displayName}</color>\n");
            }
        }

        return sb.ToString().TrimEnd('\n');
    }
}
