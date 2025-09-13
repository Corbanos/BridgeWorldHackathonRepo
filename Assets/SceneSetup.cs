using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages scene setup for both single-player and multiplayer modes
/// </summary>
public class SceneSetup : MonoBehaviour
{
    [Header("References")]
    public GameObject localPlayerPrefab;  // Assign your Player prefab in inspector
    public GameObject islandPrefab;       // Assign your island prefab, or leave empty to find existing

    [Header("Settings")]
    public Vector3 singlePlayerSpawnPosition = new Vector3(0, 2, 5);
    public Vector3 multiplayerSpawnPosition = new Vector3(0, 10, 0); // Higher spawn for multiplayer

    private GameObject localPlayerInstance;
    private GameObject islandInstance;
    private bool isMultiplayerActive = false;

    void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // Setup for single-player mode initially
        SetupSinglePlayer();
    }

    void SetupSinglePlayer()
    {
        // Ensure island exists
        EnsureIslandExists();

        // Spawn local player for single-player
        if (localPlayerPrefab != null && localPlayerInstance == null)
        {
            localPlayerInstance = Instantiate(localPlayerPrefab, singlePlayerSpawnPosition, Quaternion.identity);
            
            // Remove NetworkObject and NetworkPlayer components for single-player
            NetworkObject netObj = localPlayerInstance.GetComponent<NetworkObject>();
            if (netObj) DestroyImmediate(netObj);
            
            NetworkPlayer netPlayer = localPlayerInstance.GetComponent<NetworkPlayer>();
            if (netPlayer) DestroyImmediate(netPlayer);

            Debug.Log("[SceneSetup] Single-player setup complete");
        }
    }

    void EnsureIslandExists()
    {
        // Look for existing island by common names/tags
        GameObject existingIsland = GameObject.FindGameObjectWithTag("Island");
        if (existingIsland == null)
        {
            existingIsland = GameObject.Find("ProceduralIsland");
        }
        if (existingIsland == null)
        {
            existingIsland = GameObject.Find("Island");
        }

        if (existingIsland != null)
        {
            islandInstance = existingIsland;
            Debug.Log("[SceneSetup] Found existing island: " + existingIsland.name);
            return;
        }

        // Create island if prefab is assigned
        if (islandPrefab != null)
        {
            islandInstance = Instantiate(islandPrefab);
            Debug.Log("[SceneSetup] Created island from prefab");
        }
        else
        {
            // Create a simple placeholder island
            GameObject island = new GameObject("Island");
            
            // Add a simple terrain/plane as placeholder
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.SetParent(island.transform);
            plane.transform.localScale = new Vector3(10, 1, 10);
            plane.name = "IslandTerrain";
            
            islandInstance = island;
            Debug.Log("[SceneSetup] Created basic placeholder island");
        }
    }

    void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // We just connected to multiplayer
            isMultiplayerActive = true;

            // Remove single-player instance BEFORE network spawning happens
            if (localPlayerInstance != null)
            {
                Debug.Log("[SceneSetup] Removing single-player instance for multiplayer");
                DestroyImmediate(localPlayerInstance);
                localPlayerInstance = null;
            }

            // Disable any existing cameras to prevent conflicts
            Camera[] allCameras = FindObjectsOfType<Camera>();
            foreach (Camera cam in allCameras)
            {
                if (cam.gameObject.name != "Main Camera") // Keep scene camera as backup
                {
                    cam.enabled = false;
                }
            }
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // We disconnected from multiplayer
            isMultiplayerActive = false;

            // Restore single-player mode
            SetupSinglePlayer();
            Debug.Log("[SceneSetup] Restored single-player mode");
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}