using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen "floor cleared" screen shown once every enemy on a floor is
/// dead, before the next floor generates. Self-bootstrapping singleton;
/// GameManager's AdvanceFloorWhenSafe coroutine waits on the onContinue
/// callback before tearing the floor down.
/// </summary>
public class FloorCompleteUI : MonoBehaviour
{
    private static FloorCompleteUI instance;

    public static FloorCompleteUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("FloorCompleteUI");
                instance = go.AddComponent<FloorCompleteUI>();
            }
            return instance;
        }
    }

    private RectTransform root;
    private Text titleText;
    private Text bodyText;
    private Button continueButton;
    private Action pendingContinue;

    // Input.GetKeyDown is true for the whole frame across every script, so the
    // Space press that confirmed the last fight's outro screen must not also
    // confirm this one. See VictoryScreenUI.shownOnFrame for the full explanation.
    private int shownOnFrame = -1;

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("FloorCompleteScreen", out titleText, out bodyText, out continueButton);
        if (root == null) return;

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

    public void Show(int floorNumber, Action onContinue)
    {
        if (root == null)
        {
            onContinue?.Invoke();
            return;
        }

        pendingContinue = onContinue;
        titleText.text = $"Floor {floorNumber} Complete!";
        bodyText.text = "Every enemy on this floor is defeated.";

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        // Built once in Awake and never moved since - re-raise on every show so a
        // later-added Canvas child can't end up painting over this. Same fix
        // ItemTooltipUI uses.
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
