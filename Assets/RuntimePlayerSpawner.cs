using Unity.Netcode;
using UnityEngine;

public class RuntimePlayerSpawner : NetworkBehaviour
{
    [Header("Player Settings")]
    public float moveSpeed = 5f;
    public float mouseSensitivity = 2f;
    public bool debugSpawning = true;

    public override void OnNetworkSpawn()
    {
        // DISABLED - This conflicts with Unity's built-in player spawning
        // Use SimpleNetworkManager with Unity's NetworkManager instead
        Debug.Log("[RuntimePlayerSpawner] DISABLED - Use Unity's built-in player spawning instead");
        enabled = false;
        return;
        
        if (IsServer)
        {
            // Subscribe to client connections
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            // Spawn player for host immediately
            SpawnPlayerForClient(NetworkManager.Singleton.LocalClientId);
        }

        if (debugSpawning)
            Debug.Log($"[RuntimePlayerSpawner] Network spawned, IsServer: {IsServer}, IsClient: {IsClient}");
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    void OnClientConnected(ulong clientId)
    {
        if (debugSpawning)
            Debug.Log($"[RuntimePlayerSpawner] Client {clientId} connected, spawning player...");

        // Don't spawn again for the host (already done in OnNetworkSpawn)
        if (clientId == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost)
        {
            if (debugSpawning) Debug.Log("[RuntimePlayerSpawner] Skipping duplicate spawn for host");
            return;
        }

        // Create networked player at runtime
        SpawnPlayerForClient(clientId);
    }

    void SpawnPlayerForClient(ulong clientId)
    {
        // Create the player GameObject
        GameObject playerObject = new GameObject($"NetworkedPlayer_{clientId}");

        // Add NetworkObject component first
        NetworkObject netObj = playerObject.AddComponent<NetworkObject>();

        // Add our NetworkedPlayer script
        NetworkedPlayer playerScript = playerObject.AddComponent<NetworkedPlayer>();

        // Configure the player
        playerScript.moveSpeed = moveSpeed;
        playerScript.mouseSensitivity = mouseSensitivity;
        playerScript.debugNetworking = debugSpawning;

        // Add CharacterController
        CharacterController charController = playerObject.AddComponent<CharacterController>();
        charController.height = 2f;
        charController.radius = 0.5f;
        charController.center = new Vector3(0, 1f, 0);

        // Always create a camera child; NetworkedPlayer will enable it only for the owner
        GameObject cameraObject = new GameObject("PlayerCamera");
        cameraObject.transform.SetParent(playerObject.transform);
        cameraObject.transform.localPosition = new Vector3(0, 1.6f, 0); // Eye height

        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.enabled = false; // Start disabled, NetworkedPlayer will handle it
        playerScript.cameraTransform = cameraObject.transform;

        // Set player tag
        playerObject.tag = "Player";

        // Position the player at a valid ground position
        Vector3 spawn = GetSpawnPosition(clientId);
        playerObject.transform.position = AdjustToGround(spawn);

        // Spawn the networked object and assign ownership
        netObj.SpawnAsPlayerObject(clientId);

        if (debugSpawning)
        {
            bool isHost = NetworkManager.Singleton.IsHost && clientId == NetworkManager.Singleton.LocalClientId;
            Debug.Log($"[RuntimePlayerSpawner] Spawned {(isHost ? "HOST" : "CLIENT")} player for client {clientId} at {playerObject.transform.position}");
        }
    }

    Vector3 GetSpawnPosition(ulong clientId)
    {
        // Simple spawn positioning - spread players out
        float angle = (clientId * 45f) % 360f; // 45 degrees apart
        float radius = 5f;

        Vector3 spawnPos = new Vector3(
            Mathf.Sin(angle * Mathf.Deg2Rad) * radius,
            60f, // Start near island top height; will be adjusted
            Mathf.Cos(angle * Mathf.Deg2Rad) * radius
        );

        return spawnPos;
    }

    Vector3 AdjustToGround(Vector3 approximate)
    {
        // Raycast down from high above to find the island/ground
        Vector3 origin = new Vector3(approximate.x, approximate.y + 1000f, approximate.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Place slightly above the ground
            return new Vector3(approximate.x, hit.point.y + 0.05f, approximate.z);
        }
        // Try upward raycast if we started below terrain
        Vector3 below = new Vector3(approximate.x, approximate.y - 100f, approximate.z);
        if (Physics.Raycast(below, Vector3.up, out hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
        {
            return new Vector3(approximate.x, hit.point.y + 0.05f, approximate.z);
        }
        // Fallback: slight elevation
        return new Vector3(approximate.x, Mathf.Max(approximate.y, 5f), approximate.z);
    }
}
