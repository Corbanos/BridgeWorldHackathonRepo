using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class NetworkedBuilder : NetworkBehaviour
{
    [Header("Network Building")]
    public BuilderLite localBuilder;  // Reference to your existing BuilderLite
    
    // Network events for building synchronization
    public override void OnNetworkSpawn()
    {
        if (localBuilder != null)
        {
            // Hook into local builder events (we'll modify BuilderLite to support this)
            Debug.Log("Networked builder spawned");
        }
    }
    
    // Called when someone places an object
    [ServerRpc(RequireOwnership = false)]
    public void PlaceObjectServerRpc(string itemId, Vector3 position, Quaternion rotation, ulong playerId)
    {
        Debug.Log($"Player {playerId} placed {itemId} at {position}");
        
        // Broadcast to all clients
        PlaceObjectClientRpc(itemId, position, rotation, playerId);
    }
    
    // Called on all clients to place the object
    [ClientRpc]
    void PlaceObjectClientRpc(string itemId, Vector3 position, Quaternion rotation, ulong playerId)
    {
        // Don't duplicate on the player who placed it
        if (playerId == NetworkManager.Singleton.LocalClientId) return;
        
        // Find the prefab and instantiate it
        if (localBuilder != null)
        {
            var prefab = localBuilder.FindPrefabById(itemId);
            if (prefab != null && localBuilder.buildRoot != null)
            {
                var go = Instantiate(prefab, position, rotation, localBuilder.buildRoot);
                go.name = itemId + " (Networked)";
                
                Debug.Log($"Received networked object: {itemId} from player {playerId}");
            }
        }
    }
    
    // Called when someone deletes an object
    [ServerRpc(RequireOwnership = false)]
    public void DeleteObjectServerRpc(Vector3 position, ulong playerId)
    {
        Debug.Log($"Player {playerId} deleted object at {position}");
        
        // Broadcast to all clients
        DeleteObjectClientRpc(position, playerId);
    }
    
    [ClientRpc]
    void DeleteObjectClientRpc(Vector3 position, ulong playerId)
    {
        // Don't duplicate on the player who deleted it
        if (playerId == NetworkManager.Singleton.LocalClientId) return;
        
        // Find and delete the object at this position
        if (localBuilder != null && localBuilder.buildRoot != null)
        {
            for (int i = 0; i < localBuilder.buildRoot.childCount; i++)
            {
                var child = localBuilder.buildRoot.GetChild(i);
                if (child && Vector3.Distance(child.position, position) < 0.1f)
                {
                    Destroy(child.gameObject);
                    Debug.Log($"Deleted networked object from player {playerId}");
                    break;
                }
            }
        }
    }
    
    // Request full island sync (when joining)
    [ServerRpc(RequireOwnership = false)]
    public void RequestIslandSyncServerRpc(ulong requestingPlayerId)
    {
        Debug.Log($"Player {requestingPlayerId} requested island sync");
        
        // Send all existing objects to the requesting player
        if (localBuilder != null && localBuilder.buildRoot != null)
        {
            var syncData = new List<ObjectSyncData>();
            
            for (int i = 0; i < localBuilder.buildRoot.childCount; i++)
            {
                var child = localBuilder.buildRoot.GetChild(i);
                if (child && !child.name.Contains("(Networked)"))
                {
                    // Extract item ID
                    string itemId = child.name.Replace(" (Placed)", "").Replace("(Clone)", "").Trim();
                    
                    syncData.Add(new ObjectSyncData
                    {
                        itemId = itemId,
                        position = child.position,
                        rotation = child.rotation
                    });
                }
            }
            
            // Send sync data to requesting client
            SyncIslandDataClientRpc(syncData.ToArray(), requestingPlayerId);
        }
    }
    
    [ClientRpc]
    void SyncIslandDataClientRpc(ObjectSyncData[] syncData, ulong targetPlayerId)
    {
        // Only process if this message is for us
        if (NetworkManager.Singleton.LocalClientId != targetPlayerId) return;
        
        Debug.Log($"Received island sync data: {syncData.Length} objects");
        
        // Clear existing networked objects
        if (localBuilder != null && localBuilder.buildRoot != null)
        {
            for (int i = localBuilder.buildRoot.childCount - 1; i >= 0; i--)
            {
                var child = localBuilder.buildRoot.GetChild(i);
                if (child && child.name.Contains("(Networked)"))
                {
                    DestroyImmediate(child.gameObject);
                }
            }
            
            // Spawn synced objects
            foreach (var data in syncData)
            {
                var prefab = localBuilder.FindPrefabById(data.itemId);
                if (prefab != null)
                {
                    var go = Instantiate(prefab, data.position, data.rotation, localBuilder.buildRoot);
                    go.name = data.itemId + " (Networked)";
                }
            }
        }
    }
    
    // Helper method to notify network when local player places something
    public void NotifyObjectPlaced(string itemId, Vector3 position, Quaternion rotation)
    {
        if (IsSpawned)
        {
            PlaceObjectServerRpc(itemId, position, rotation, NetworkManager.Singleton.LocalClientId);
        }
    }
    
    // Helper method to notify network when local player deletes something
    public void NotifyObjectDeleted(Vector3 position)
    {
        if (IsSpawned)
        {
            DeleteObjectServerRpc(position, NetworkManager.Singleton.LocalClientId);
        }
    }
    
    // Request sync when we connect
    void Start()
    {
        if (IsClient)
        {
            // Small delay to ensure everything is set up
            Invoke(nameof(RequestSync), 1f);
        }
    }
    
    void RequestSync()
    {
        if (IsSpawned && !IsHost)
        {
            RequestIslandSyncServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    }
}

[System.Serializable]
public struct ObjectSyncData : INetworkSerializable
{
    public string itemId;
    public Vector3 position;
    public Quaternion rotation;
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref rotation);
    }
}