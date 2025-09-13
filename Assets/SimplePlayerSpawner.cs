using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class SimplePlayerSpawner : NetworkBehaviour
{
    private bool hasSpawnedLocalPlayer = false;

    void Start()
    {
        // Wait a frame then check if we should spawn
        StartCoroutine(CheckAndSpawn());
    }

    IEnumerator CheckAndSpawn()
    {
        // Wait for network to be ready
        yield return new WaitForSeconds(0.5f);

        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log($"[SimplePlayerSpawner] Network ready. IsHost: {NetworkManager.Singleton.IsHost}, IsClient: {NetworkManager.Singleton.IsClient}");

        // If we're the host, spawn our player
        if (NetworkManager.Singleton.IsHost && !hasSpawnedLocalPlayer)
        {
            SpawnLocalPlayer();
        }

        // Subscribe to client connections if we're the server
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        Debug.Log($"[SimplePlayerSpawner] Client {clientId} connected!");

        // Spawn player for the newly connected client
        if (clientId != NetworkManager.Singleton.LocalClientId)
        {
            SpawnPlayerForClient(clientId);
        }
    }

    void SpawnLocalPlayer()
    {
        if (hasSpawnedLocalPlayer) return;
        hasSpawnedLocalPlayer = true;

        if (NetworkManager.Singleton.IsServer)
        {
            SpawnPlayerForClient(NetworkManager.Singleton.LocalClientId);
        }
    }

    void SpawnPlayerForClient(ulong clientId)
    {
        Debug.Log($"[SimplePlayerSpawner] Creating player for client {clientId}");

        // Create player GameObject
        GameObject playerObj = new GameObject($"NetworkPlayer_{clientId}");

        // Add NetworkObject
        NetworkObject netObj = playerObj.AddComponent<NetworkObject>();

        // Add NetworkedPlayer
        NetworkedPlayer netPlayer = playerObj.AddComponent<NetworkedPlayer>();
        netPlayer.debugNetworking = true;

        // Add CharacterController
        CharacterController controller = playerObj.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = new Vector3(0, 1f, 0);

        // Position the player around the island, then adjust to island surface
        float angle = clientId * 90f;
        Vector3 approxPos = new Vector3(
            Mathf.Sin(angle * Mathf.Deg2Rad) * 15f,  // Further from center
            100f,  // Start higher to ensure we're above everything
            Mathf.Cos(angle * Mathf.Deg2Rad) * 15f
        );
        Vector3 spawnPos = AdjustToGround(approxPos);
        playerObj.transform.position = spawnPos;

        // Always create a camera child; NetworkedPlayer will enable only for the owner
        GameObject camObj = new GameObject("PlayerCamera");
        camObj.transform.SetParent(playerObj.transform);
        camObj.transform.localPosition = new Vector3(0, 1.6f, 0);
        Camera cam = camObj.AddComponent<Camera>();
        cam.enabled = false; // Will be handled by NetworkedPlayer
        netPlayer.cameraTransform = camObj.transform;

        // Tag it
        playerObj.tag = "Player";

        // Spawn on network
        netObj.SpawnAsPlayerObject(clientId);

        Debug.Log($"[SimplePlayerSpawner] Spawned player for client {clientId} at {spawnPos}");
    }

    Vector3 AdjustToGround(Vector3 approximate)
    {
        // Start from a very high position to ensure we're above any terrain
        Vector3 origin = new Vector3(approximate.x, approximate.y + 2000f, approximate.z);
        
        // Raycast down to find the ground/island
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Place the character controller 2 units above the ground
            Vector3 groundPos = new Vector3(approximate.x, hit.point.y + 2f, approximate.z);
            Debug.Log($"[SimplePlayerSpawner] Found ground at {hit.point.y}, placing player at {groundPos.y}");
            return groundPos;
        }
        
        // If no ground found, try from below
        Vector3 below = new Vector3(approximate.x, approximate.y - 100f, approximate.z);
        if (Physics.Raycast(below, Vector3.up, out hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 groundPos = new Vector3(approximate.x, hit.point.y + 2f, approximate.z);
            Debug.Log($"[SimplePlayerSpawner] Found ground from below at {hit.point.y}, placing player at {groundPos.y}");
            return groundPos;
        }
        
        // Ultimate fallback: use a safe height
        Vector3 fallbackPos = new Vector3(approximate.x, 10f, approximate.z);
        Debug.LogWarning($"[SimplePlayerSpawner] No ground found, using fallback position {fallbackPos}");
        return fallbackPos;
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}
