using UnityEngine;

/// <summary>Keeps the camera centered on the player each frame. Attach to the Main Camera.</summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private float zOffset = -10f;
    private Transform target;

    private void LateUpdate()
    {
        if (target == null)
        {
            // Cheap property lookup instead of a per-frame FindObjectOfType scan,
            // which would otherwise run forever once the player dies.
            PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) return;
            target = player.transform;
        }

        Vector3 shake = CameraShake.CurrentOffset;
        transform.position = new Vector3(target.position.x + shake.x, target.position.y + shake.y, zOffset);
    }
}
