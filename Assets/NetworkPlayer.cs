using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Components")]
    private Camera playerCamera;
    private PlayerController playerController;
    private GameObject visualRepresentation;

    [Header("Networking")]
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    void Start()
    {
        // Get references
        playerCamera = GetComponentInChildren<Camera>();
        playerController = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // This is our player - enable controls
            if (playerController) playerController.enabled = true;
            if (playerCamera) playerCamera.enabled = true;

            // Hide our own visual
            HideVisual();

            Debug.Log("[NetworkPlayer] Local player configured");
        }
        else
        {
            // This is a remote player - disable controls
            if (playerController) playerController.enabled = false;
            if (playerCamera) playerCamera.enabled = false;

            // Show visual for remote player
            ShowVisual();

            Debug.Log($"[NetworkPlayer] Remote player {OwnerClientId} configured");
        }

        // Subscribe to network variable changes
        networkPosition.OnValueChanged += OnPositionChanged;
        networkRotation.OnValueChanged += OnRotationChanged;
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from network variable changes
        networkPosition.OnValueChanged -= OnPositionChanged;
        networkRotation.OnValueChanged -= OnRotationChanged;
    }

    void Update()
    {
        if (IsOwner)
        {
            // Update network position for owner
            if (Vector3.Distance(transform.position, networkPosition.Value) > 0.1f)
            {
                networkPosition.Value = transform.position;
            }

            if (Quaternion.Angle(transform.rotation, networkRotation.Value) > 1f)
            {
                networkRotation.Value = transform.rotation;
            }
        }
    }

    void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner)
        {
            transform.position = newValue;
        }
    }

    void OnRotationChanged(Quaternion previousValue, Quaternion newValue)
    {
        if (!IsOwner)
        {
            transform.rotation = newValue;
        }
    }

    void ShowVisual()
    {
        if (visualRepresentation == null)
        {
            // Create a simple capsule as visual representation
            visualRepresentation = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visualRepresentation.transform.SetParent(transform);
            visualRepresentation.transform.localPosition = Vector3.up;
            visualRepresentation.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            visualRepresentation.name = "PlayerVisual";

            // Remove the collider from the visual mesh (CharacterController handles collision)
            Collider meshCollider = visualRepresentation.GetComponent<Collider>();
            if (meshCollider) DestroyImmediate(meshCollider);

            // Make it bright and visible
            Renderer renderer = visualRepresentation.GetComponent<Renderer>();
            if (renderer)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(1f, 0f, 0f, 1f); // Bright red for remote players
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.3f);
                renderer.material = mat;
                
                // Ensure it's on the default layer (visible to all cameras)
                visualRepresentation.layer = 0;
            }

            Debug.Log($"[NetworkPlayer] Created visual for remote player {OwnerClientId}");
        }

        if (visualRepresentation != null)
        {
            visualRepresentation.SetActive(true);
            Debug.Log($"[NetworkPlayer] Activated visual for remote player {OwnerClientId} at {transform.position}");
        }
    }

    void HideVisual()
    {
        if (visualRepresentation != null)
        {
            visualRepresentation.SetActive(false);
        }
    }
}