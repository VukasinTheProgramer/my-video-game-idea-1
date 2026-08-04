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

    private void Awake()
    {
        root = ModalScreenUI.BuildOverlay("FloorCompleteScreen", out titleText, out bodyText, out continueButton);
        if (root == null) return;

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
