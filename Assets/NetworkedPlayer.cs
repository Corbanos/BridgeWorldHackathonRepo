using Unity.Netcode;
using UnityEngine;

public class NetworkedPlayer : NetworkBehaviour
{
    [Header("Player Settings")]
    public float moveSpeed = 5f;
    public float mouseSensitivity = 2f;
    public Transform cameraTransform;
    
    [Header("Visual Settings")]
    public GameObject playerMesh; // Visual representation
    public Material localPlayerMaterial;
    public Material remotePlayerMaterial;
    
    [Header("Network Settings")]
    public bool disableLocalPlayerOnClients = true;
    public bool debugNetworking = true;
    
    // Network variables for position and rotation
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkCameraRotation = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    
    // Local player reference
    private CharacterController characterController;
    private Camera playerCamera;
    
    // Mouse look variables
    private float xRotation = 0f;
    
    public override void OnNetworkSpawn()
    {
        // Get components
        characterController = GetComponent<CharacterController>();
        if (!characterController)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        // Find camera - ONLY if it's a direct child of THIS gameObject; create one if missing
        if (!cameraTransform)
        {
            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                if (cam.transform.parent == transform)
                {
                    playerCamera = cam;
                    cameraTransform = cam.transform;
                    break;
                }
            }
            if (!playerCamera)
            {
                var camObj = new GameObject("PlayerCamera");
                camObj.transform.SetParent(transform);
                camObj.transform.localPosition = new Vector3(0, 1.6f, 0);
                playerCamera = camObj.AddComponent<Camera>();
                cameraTransform = camObj.transform;
                playerCamera.enabled = false;
            }
        }

        // Create a simple visual representation if none exists
        SetupPlayerVisual();

        // Make sure visual is visible for remote players
        if (!IsOwner && playerMesh)
        {
            playerMesh.SetActive(true);
            Renderer meshRenderer = playerMesh.GetComponent<Renderer>();
            if (meshRenderer)
            {
                meshRenderer.enabled = true;
            }
        }

        if (IsOwner)
        {
            // Owner uses its own camera
            if (playerCamera)
            {
                playerCamera.enabled = true;
                playerCamera.tag = "MainCamera";
            }
            // Initialize network transform
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
            if (cameraTransform)
            {
                var e = cameraTransform.localRotation.eulerAngles;
                networkCameraRotation.Value = new Vector3(e.x, e.y, e.z);
            }
            if (debugNetworking) Debug.Log($"[NetworkedPlayer] Local player ready with camera at {transform.position}");
        }
        else
        {
            // This is another player - disable ONLY their camera, not the host's
            if (playerCamera)
            {
                playerCamera.enabled = false;
                if (playerCamera.CompareTag("MainCamera")) playerCamera.tag = "Untagged";
            }
            
            // Disable local player scripts that might interfere
            if (disableLocalPlayerOnClients)
            {
                var localPlayerScripts = GetComponents<MonoBehaviour>();
                foreach (var script in localPlayerScripts)
                {
                    // Don't disable this NetworkedPlayer script or camera protection
                    if (script != this &&
                        script.GetType().Name.Contains("Player") &&
                        !script.GetType().Name.Contains("CameraProtection"))
                    {
                        script.enabled = false;
                    }
                }
            }
            
            if (debugNetworking) Debug.Log($"[NetworkedPlayer] Remote player spawned (Player {OwnerClientId})");
        }
        
        // Set initial position from network if not owner
        if (!IsOwner)
        {
            // Always use network position for remote players
            if (networkPosition.Value != Vector3.zero)
            {
                transform.position = networkPosition.Value;
                transform.rotation = networkRotation.Value;
                Debug.Log($"[NetworkedPlayer] Remote player {OwnerClientId} positioned at {networkPosition.Value}");
            }
            else
            {
                Debug.LogWarning($"[NetworkedPlayer] Remote player {OwnerClientId} has zero position!");
            }

            if (cameraTransform && networkCameraRotation.Value != Vector3.zero)
            {
                cameraTransform.localRotation = Quaternion.Euler(networkCameraRotation.Value);
            }
        }
        
        // Subscribe to network variable changes
        networkPosition.OnValueChanged += OnPositionChanged;
        networkRotation.OnValueChanged += OnRotationChanged;
        networkCameraRotation.OnValueChanged += OnCameraRotationChanged;
        
        // Force visual update
        UpdatePlayerVisual();
    }
    
    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= OnPositionChanged;
        networkRotation.OnValueChanged -= OnRotationChanged;
        networkCameraRotation.OnValueChanged -= OnCameraRotationChanged;

        // Don't mess with cameras on despawn - let the original player keep control
    }
    
    void Update()
    {
        if (IsOwner)
        {
            HandleMouseLook();
            HandleMovement();
            UpdateNetworkTransform();
        }
        else
        {
            // Remote player - ensure visual stays visible
            if (playerMesh && !playerMesh.activeInHierarchy)
            {
                playerMesh.SetActive(true);
            }
        }
    }
    
    void HandleMovement()
    {
        if (!characterController) return;
        
        // Get input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        // Calculate movement direction
        Vector3 direction = transform.right * horizontal + transform.forward * vertical;
        direction = Vector3.ClampMagnitude(direction, 1f);
        
        // Apply movement
        Vector3 movement = direction * moveSpeed * Time.deltaTime;
        
        // Add gravity
        movement.y = -9.81f * Time.deltaTime;
        
        characterController.Move(movement);
    }
    
    void HandleMouseLook()
    {
        if (!cameraTransform) return;
        
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // Rotate the player body left/right
        transform.Rotate(Vector3.up * mouseX);
        
        // Rotate the camera up/down
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
    
    void UpdateNetworkTransform()
    {
        if (!IsOwner) return;
        // Push current transform and camera rotation to network variables
        networkPosition.Value = transform.position;
        networkRotation.Value = transform.rotation;
        if (cameraTransform)
        {
            var e = cameraTransform.localRotation.eulerAngles;
            networkCameraRotation.Value = new Vector3(e.x, e.y, e.z);
        }
    }
    
    void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner)
        {
            // Directly set position for more accurate replication
            transform.position = newValue;
        }
    }
    
    void OnRotationChanged(Quaternion previousValue, Quaternion newValue)
    {
        if (!IsOwner)
        {
            // Directly set rotation for more accurate replication
            transform.rotation = newValue;
        }
    }
    
    void OnCameraRotationChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner && cameraTransform)
        {
            // Update camera rotation for other players (for animation purposes)
            cameraTransform.localRotation = Quaternion.Lerp(cameraTransform.localRotation, Quaternion.Euler(newValue), Time.deltaTime * 10f);
        }
    }
    
    void SetupPlayerVisual()
    {
        if (playerMesh == null)
        {
            // Create a simple capsule as visual representation
            playerMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerMesh.transform.SetParent(transform);
            playerMesh.transform.localPosition = Vector3.up;
            playerMesh.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            playerMesh.name = "PlayerVisual";

            // Remove the collider from the visual mesh (CharacterController handles collision)
            Collider meshCollider = playerMesh.GetComponent<Collider>();
            if (meshCollider) DestroyImmediate(meshCollider);

            // Make sure the visual is on a layer that's visible to all cameras
            playerMesh.layer = 0; // Default layer

            if (debugNetworking) Debug.Log("[NetworkedPlayer] Created default player visual");
        }
    }
    
    void UpdatePlayerVisual()
    {
        if (playerMesh == null) return;

        Renderer meshRenderer = playerMesh.GetComponent<Renderer>();
        if (meshRenderer)
        {
            // Ensure the material supports transparency
            Material mat = new Material(Shader.Find("Standard"));

            if (IsOwner)
            {
                // Local player - use local material or make semi-transparent
                if (localPlayerMaterial)
                {
                    meshRenderer.material = localPlayerMaterial;
                }
                else
                {
                    // Make local player semi-transparent blue
                    mat.color = new Color(0.3f, 0.7f, 1f, 0.7f);
                    mat.SetFloat("_Mode", 3); // Set to transparent mode
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    meshRenderer.material = mat;
                }
            }
            else
            {
                // Remote player - use remote material or make opaque
                if (remotePlayerMaterial)
                {
                    meshRenderer.material = remotePlayerMaterial;
                }
                else
                {
                    // Make remote player solid red
                    mat.color = new Color(1f, 0.3f, 0.3f, 1f);
                    meshRenderer.material = mat;
                }
            }
        }

        if (debugNetworking)
        {
            string playerType = IsOwner ? "LOCAL" : "REMOTE";
            Debug.Log($"[NetworkedPlayer] Updated {playerType} player visual at {transform.position}");
        }
    }
    
    // Debug visualization
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        
        if (IsOwner)
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }
        
        Gizmos.DrawWireCube(transform.position + Vector3.up, Vector3.one);
        
        // Draw network position if different
        if (!IsOwner && Vector3.Distance(transform.position, networkPosition.Value) > 0.1f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(networkPosition.Value + Vector3.up, 0.5f);
        }
    }
}
