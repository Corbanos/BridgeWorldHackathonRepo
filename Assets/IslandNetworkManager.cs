using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class IslandNetworkManager : MonoBehaviour
{
    [Header("DISABLED - Use SimpleNetworkManager instead")]
    public bool enabled = false;
    [Header("Island System")]
    public Transform islandSpawnPoint;
    public GameObject bridgePrefab;
    public float islandSpacing = 50f;
    
    [Header("Player Spawning")]
    public RuntimePlayerSpawner playerSpawner;
    
    [Header("Network Settings")]
    public string serverAddress = "127.0.0.1";
    public ushort serverPort = 7777;
    
    // Island tracking
    private Dictionary<ulong, Vector3> playerIslands = new Dictionary<ulong, Vector3>();
    private Dictionary<ulong, GameObject> playerBridges = new Dictionary<ulong, GameObject>();
    
    // Network variables
    private NetworkVariable<int> connectedPlayers = new NetworkVariable<int>(0);
    
    // UI state
    private bool showNetworkUI = false;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    
    void Start()
    {
        // DISABLED - This conflicts with SimpleNetworkManager
        // Use SimpleNetworkManager instead for the working multiplayer setup
        Debug.Log("[IslandNetworkManager] DISABLED - Use SimpleNetworkManager instead");
        enabled = false;
        return;
        
        // Set up network events
        Unity.Netcode.NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        Unity.Netcode.NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Do NOT auto-add other systems here; keep single source of truth (scene should contain exactly one spawner and one menu)
    }
    
    void Update()
    {
        // Toggle network UI with J key
        if (Input.GetKeyDown(KeyCode.J))
        {
            showNetworkUI = !showNetworkUI;
            
            if (showNetworkUI)
            {
                // Save current cursor state and show cursor for UI
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                // Restore previous cursor state
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
            }
        }
        
        // Auto-hide UI when connected (unless J is held)
        if (showNetworkUI && (Unity.Netcode.NetworkManager.Singleton.IsClient || Unity.Netcode.NetworkManager.Singleton.IsServer))
        {
            if (!Input.GetKey(KeyCode.J))
            {
                showNetworkUI = false;
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
            }
        }
    }
    
    void OnGUI()
    {
        // Always show the key hint
        GUI.color = new Color(1, 1, 1, 0.7f);
        GUI.Label(new Rect(10, Screen.height - 25, 200, 25), "Press J for Network Menu");
        GUI.color = Color.white;
        
        // Only show network UI if toggled on
        if (!showNetworkUI) return;
        
        GUILayout.BeginArea(new Rect(10, 100, 300, 200));
        
        if (!Unity.Netcode.NetworkManager.Singleton.IsClient && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            // Not connected - show connection options
            GUILayout.Label("Island Network System", GUI.skin.box);
            GUILayout.Space(10);
            
            if (GUILayout.Button("Host Island (Server + Client)", GUILayout.Height(30)))
            {
                StartHost();
            }
            
            GUILayout.Space(5);
            
            if (GUILayout.Button("Join Island (Client)", GUILayout.Height(30)))
            {
                StartClient();
            }
            
            GUILayout.Space(10);
            GUILayout.Label("Server Address:");
            serverAddress = GUILayout.TextField(serverAddress);
            
            GUILayout.Space(10);
            GUI.color = Color.yellow;
            GUILayout.Label("Press J again to close menu");
            GUI.color = Color.white;
        }
        else
        {
            // Connected - show status
            GUILayout.Label("Connected to Island Network", GUI.skin.box);
            GUILayout.Space(10);
            GUILayout.Label($"Connected Players: {connectedPlayers.Value}");
            GUILayout.Label($"My Island ID: {Unity.Netcode.NetworkManager.Singleton.LocalClientId}");
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Disconnect", GUILayout.Height(30)))
            {
                Unity.Netcode.NetworkManager.Singleton.Shutdown();
                showNetworkUI = false; // Hide UI after disconnect
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
            }
            
            GUILayout.Space(10);
            GUI.color = Color.yellow;
            GUILayout.Label("Press J to close menu");
            GUI.color = Color.white;
        }
        
        GUILayout.EndArea();
    }
    
    void StartHost()
    {
        Unity.Netcode.NetworkManager.Singleton.StartHost();
        Debug.Log("Started hosting island...");
    }
    
    void StartClient()
    {
        Unity.Netcode.NetworkManager.Singleton.StartClient();
        Debug.Log("Connecting to island network...");
    }
    
    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Player {clientId} connected to island network!");
        
        if (Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            connectedPlayers.Value++;
            AssignIslandToPlayer(clientId);
            CreateBridgeToPlayer(clientId);
        }
    }
    
    void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Player {clientId} disconnected from island network!");
        
        if (Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            connectedPlayers.Value--;
            RemovePlayerIsland(clientId);
        }
    }
    
    void AssignIslandToPlayer(ulong clientId)
    {
        // Calculate island position in a circle around the center
        float angle = (360f / 8f) * (clientId % 8); // Max 8 islands in circle
        float radius = islandSpacing;
        
        Vector3 islandPosition = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
            0f,
            Mathf.Sin(angle * Mathf.Deg2Rad) * radius
        );
        
        playerIslands[clientId] = islandPosition;
        
        // If this is our own client, set island position directly
        if (Unity.Netcode.NetworkManager.Singleton.LocalClientId == clientId)
        {
            if (islandSpawnPoint != null)
            {
                islandSpawnPoint.position = islandPosition;
            }
            Debug.Log($"My island assigned to position: {islandPosition}");
        }
    }
    
    void CreateBridgeToPlayer(ulong clientId)
    {
        if (clientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId) return; // Don't bridge to self
        
        if (playerIslands.ContainsKey(clientId) && bridgePrefab != null)
        {
            Vector3 myPosition = Vector3.zero; // Center island for host
            Vector3 theirPosition = playerIslands[clientId];
            
            // Create bridge between islands
            Vector3 bridgePosition = (myPosition + theirPosition) / 2f;
            Quaternion bridgeRotation = Quaternion.LookRotation(theirPosition - myPosition);
            
            GameObject bridge = Instantiate(bridgePrefab, bridgePosition, bridgeRotation);
            
            // Scale bridge to span the distance
            float distance = Vector3.Distance(myPosition, theirPosition);
            bridge.transform.localScale = new Vector3(1f, 1f, distance / bridgePrefab.transform.localScale.z);
            
            playerBridges[clientId] = bridge;
            
            Debug.Log($"Created bridge to player {clientId}");
        }
    }
    
    void RemovePlayerIsland(ulong clientId)
    {
        if (playerIslands.ContainsKey(clientId))
        {
            playerIslands.Remove(clientId);
        }
        
        if (playerBridges.ContainsKey(clientId))
        {
            if (playerBridges[clientId] != null)
            {
                Destroy(playerBridges[clientId]);
            }
            playerBridges.Remove(clientId);
        }
    }
    
    public Vector3 GetMyIslandPosition()
    {
        ulong myId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
        if (playerIslands.ContainsKey(myId))
        {
            return playerIslands[myId];
        }
        return Vector3.zero;
    }
    
    public List<ulong> GetConnectedPlayerIds()
    {
        return new List<ulong>(playerIslands.Keys);
    }
}
