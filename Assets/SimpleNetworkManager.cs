using Unity.Netcode;
using UnityEngine;

public class SimpleNetworkManager : MonoBehaviour
{
    private bool showUI = false;  // Start with UI hidden

    void Start()
    {
        Debug.Log("[SimpleNetworkManager] Starting up - Press Tab for multiplayer menu");
    }

    void Update()
    {
        // Toggle menu with Tab key
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            showUI = !showUI;
            UpdateCursorState();
        }

        // Quick keyboard shortcuts (only when menu is visible)
        if (showUI && !NetworkManager.Singleton.IsListening)
        {
            if (Input.GetKeyDown(KeyCode.H))
            {
                StartHost();
            }
            else if (Input.GetKeyDown(KeyCode.J))
            {
                StartClient();
            }
        }
    }

    void StartHost()
    {
        Debug.Log("[SimpleNetworkManager] Starting Host...");
        NetworkManager.Singleton.StartHost();
        showUI = false;
        UpdateCursorState();
    }

    void StartClient()
    {
        Debug.Log("[SimpleNetworkManager] Starting Client...");
        
        // Make sure we're connecting to the host's IP
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            // Default to localhost for testing
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
            Debug.Log($"[SimpleNetworkManager] Connecting to {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
        }
        
        NetworkManager.Singleton.StartClient();
        showUI = false;
        UpdateCursorState();
    }

    void UpdateCursorState()
    {
        if (showUI)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Box("Multiplayer Menu");

        if (!NetworkManager.Singleton.IsListening)
        {
            if (GUILayout.Button("Host Game"))
            {
                StartHost();
            }

            if (GUILayout.Button("Join Game"))
            {
                StartClient();
            }

            GUILayout.Space(10);
            GUILayout.Label("Press Tab to hide this menu");
        }
        else
        {
            GUI.color = new Color(1, 1, 1, 0.5f);
            GUILayout.Label("Press Tab for multiplayer menu");
            GUI.color = Color.white;
        }

        GUILayout.EndArea();

        // Show status in corner
        if (NetworkManager.Singleton.IsListening)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 200, 10, 190, 100));
            
            if (NetworkManager.Singleton.IsHost)
            {
                GUI.color = Color.green;
                GUILayout.Label("STATUS: HOSTING");
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                GUI.color = Color.cyan;
                GUILayout.Label("STATUS: CONNECTED");
            }
            
            GUI.color = Color.white;
            GUILayout.Label($"Players: {NetworkManager.Singleton.ConnectedClientsList.Count}");

            if (GUILayout.Button("Disconnect"))
            {
                NetworkManager.Singleton.Shutdown();
            }

            GUILayout.Space(10);
            GUI.color = new Color(1, 1, 1, 0.5f);
            GUILayout.Label("Press Tab to toggle menu");
            GUI.color = Color.white;

            GUILayout.EndArea();
        }
    }
}