using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shows a death message on player death; press R to restart the scene.</summary>
[RequireComponent(typeof(Text))]
public class GameOverUI : MonoBehaviour
{
    private Text label;
    private bool isGameOver;

    private void Awake()
    {
        label = GetComponent<Text>();
        label.text = string.Empty;
    }

    private void Start()
    {
        // Bind through GameManager (its Awake always precedes any Start) rather
        // than FindObjectOfType here - the player may not exist yet at this point.
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.Player != null) Bind(GameManager.Instance.Player);
        else GameManager.Instance.OnPlayerSpawned += Bind;
    }

    private void Bind(PlayerController player)
    {
        player.OnDeath += HandlePlayerDeath;
    }

    private void HandlePlayerDeath(Entity deadPlayer)
    {
        isGameOver = true;
        label.text = "You Died — Press R to Restart";
    }

    private void Update()
    {
        if (isGameOver && Input.GetKeyDown(KeyCode.R))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
