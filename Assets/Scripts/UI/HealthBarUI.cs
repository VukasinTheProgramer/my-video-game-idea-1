using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal health bar: attach to a UI Slider. Tracks an explicitly assigned
/// Entity if set in the inspector, otherwise finds the player at runtime
/// (the player is prefab-instantiated, so there's nothing to assign in the
/// scene ahead of time). Subscribes to Entity.OnHealthChanged so it updates
/// automatically on damage/heal.
/// </summary>
[RequireComponent(typeof(Slider))]
public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private Entity target;

    private Slider slider;

    private void Awake()
    {
        slider = GetComponent<Slider>();
    }

    private void Start()
    {
        if (target != null)
        {
            Bind(target);
            return;
        }

        // Bind through GameManager (its Awake always precedes any Start) rather
        // than FindObjectOfType here - the player may not exist yet at this point.
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.Player != null) Bind(GameManager.Instance.Player);
        else GameManager.Instance.OnPlayerSpawned += Bind;
    }

    private void Bind(Entity entity)
    {
        target = entity;
        target.OnHealthChanged += HandleHealthChanged;
        HandleHealthChanged(target, target.CurrentHealth, target.MaxHealth);
    }

    private void OnDestroy()
    {
        if (target == null) return;
        target.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(Entity entity, int current, int max)
    {
        slider.maxValue = max;
        slider.value = current;
    }
}
