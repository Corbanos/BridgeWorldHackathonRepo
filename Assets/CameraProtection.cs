using UnityEngine;

/// <summary>
/// Protects the main player camera from being disabled by networking code
/// </summary>
public class CameraProtection : MonoBehaviour
{
    private Camera protectedCamera;
    private bool wasEnabled = true;

    void Start()
    {
        // Find and protect the main camera
        protectedCamera = GetComponent<Camera>();
        if (!protectedCamera)
        {
            protectedCamera = GetComponentInChildren<Camera>();
        }

        if (protectedCamera)
        {
            wasEnabled = protectedCamera.enabled;
            Debug.Log($"[CameraProtection] Protecting camera: {protectedCamera.name}");
        }
    }

    void LateUpdate()
    {
        // If this is the original player's camera and it gets disabled, re-enable it
        if (protectedCamera && wasEnabled && !protectedCamera.enabled)
        {
            Debug.LogWarning($"[CameraProtection] Camera was disabled! Re-enabling {protectedCamera.name}");
            protectedCamera.enabled = true;

            // Also ensure culling mask and other settings are correct
            if (protectedCamera.cullingMask == 0)
            {
                protectedCamera.cullingMask = -1; // Show all layers
                Debug.LogWarning("[CameraProtection] Fixed culling mask!");
            }
        }
    }

    void OnDestroy()
    {
        if (protectedCamera)
        {
            Debug.Log($"[CameraProtection] Stopped protecting camera: {protectedCamera.name}");
        }
    }
}