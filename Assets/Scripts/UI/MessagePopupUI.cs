using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen popup showing an arbitrary title/message - used for a SignPost's
/// text, a LootChest's contents summary, and GameManager's one-time welcome
/// message. Self-bootstrapping singleton, same shape as FloorCompleteUI/
/// VictoryScreenUI/LevelUpUI, but with no gameplay callback - showing one of
/// these has no effect beyond closing the popup, so unlike those it doesn't
/// freeze anything on its own (nothing needs to: opening it never spent a turn,
/// so the turn system has nothing to be frozen against).
/// </summary>
public class MessagePopupUI : MonoBehaviour
{
    private static MessagePopupUI instance;

    public static MessagePopupUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("MessagePopupUI");
                instance = go.AddComponent<MessagePopupUI>();
            }
            return instance;
        }
    }

    private RectTransform root;
    private Text titleText;
    private Text bodyText;
    private Button closeButton;
    private Coroutine autoDismissRoutine;

    // Same reasoning as FloorCompleteUI.shownOnFrame - the click/keypress that
    // opened this must not also be read as the input that closes it.
    private int shownOnFrame = -1;

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("MessagePopupScreen", out titleText, out bodyText, out closeButton);
        if (root == null) return;

        closeButton.GetComponentInChildren<Text>().text = "Close";
        closeButton.onClick.AddListener(HandleCloseClicked);
        root.gameObject.SetActive(false);
    }

    private bool CanAcceptInput() =>
        root != null && root.gameObject.activeSelf && Time.frameCount != shownOnFrame;

    private void Update()
    {
        if (!CanAcceptInput()) return;
        // Deliberately NOT KeyCode.E: E is also the interact key (PlayerController),
        // and a sign (unlike a one-time LootChest) never unregisters itself - closing
        // with E while still standing in its range would immediately re-trigger
        // Interact() and reopen the very popup E just closed.
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            HandleCloseClicked();
        }
    }

    /// <summary>autoDismissSeconds &lt;= 0 (the default) waits for a manual close -
    /// used for a sign's text and the welcome message, both meant to be read at
    /// leisure. LootChest passes a positive value so its loot summary doesn't
    /// require a button press to get back to playing.</summary>
    public void Show(string title, string message, float autoDismissSeconds = 0f)
    {
        if (root == null) return;

        if (autoDismissRoutine != null)
        {
            StopCoroutine(autoDismissRoutine);
            autoDismissRoutine = null;
        }

        titleText.text = title;
        bodyText.text = message;

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        root.transform.SetAsLastSibling();
        shownOnFrame = Time.frameCount;
        root.gameObject.SetActive(true);

        if (autoDismissSeconds > 0f)
        {
            autoDismissRoutine = StartCoroutine(AutoDismissAfter(autoDismissSeconds));
        }
    }

    private IEnumerator AutoDismissAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        autoDismissRoutine = null;
        HandleCloseClicked();
    }

    private void HandleCloseClicked()
    {
        // A manual close (button/Space/Return) before the timer fires must cancel
        // it - otherwise the stale coroutine closes an already-closed (or, worse,
        // since-reopened-for-something-else) popup seconds later.
        if (autoDismissRoutine != null)
        {
            StopCoroutine(autoDismissRoutine);
            autoDismissRoutine = null;
        }
        root.gameObject.SetActive(false);
    }
}
