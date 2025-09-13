using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class NetworkBuildSystem : NetworkBehaviour
{
    [Header("Building Settings")]
    public GameObject[] placeablePrefabs;  // Assign your placeable objects here
    public float placementRange = 10f;
    public LayerMask placementLayer = -1;

    [Header("Visual Feedback")]
    public Material previewMaterial;

    private int currentPrefabIndex = 0;
    private GameObject currentPreview;
    private bool isBuilding = false;
    private Camera playerCamera;

    // Track all placed objects
    private static List<GameObject> placedObjects = new List<GameObject>();

    void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>();

        // Make sure all placeable prefabs have NetworkObject
        foreach (var prefab in placeablePrefabs)
        {
            if (prefab && !prefab.GetComponent<NetworkObject>())
            {
                Debug.LogWarning($"[NetworkBuildSystem] {prefab.name} needs NetworkObject component for multiplayer!");
            }
        }
    }

    void Start()
    {
        // Early-exit if disabled
        if (!enabled) return;
    }

    void Update()
    {
        if (!enabled) return;

        // Toggle build mode with B key
        if (Input.GetKeyDown(KeyCode.B))
        {
            isBuilding = !isBuilding;
            if (isBuilding)
            {
                Debug.Log("[NetworkBuildSystem] Build mode ON");
            }
            else
            {
                Debug.Log("[NetworkBuildSystem] Build mode OFF");
                if (currentPreview) currentPreview.SetActive(false);
            }
        }

        if (!isBuilding) return;

        // Scroll to change object
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            ChangePrefab(scroll > 0f ? 1 : -1);
        }

        // R to rotate preview
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (currentPreview)
            {
                currentPreview.transform.Rotate(0, 90, 0);
            }
        }

        // Update preview
        UpdatePreview();

        // Place/remove objects
        if (Input.GetMouseButtonDown(0))
        {
            TryPlaceObject();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            TryRemoveObject();
        }
    }

    void ChangePrefab(int direction)
    {
        if (placeablePrefabs.Length == 0) return;

        currentPrefabIndex += direction;
        if (currentPrefabIndex >= placeablePrefabs.Length)
            currentPrefabIndex = 0;
        if (currentPrefabIndex < 0)
            currentPrefabIndex = placeablePrefabs.Length - 1;

        // Update preview
        if (currentPreview) Destroy(currentPreview);
        CreatePreview();
    }

    void CreatePreview()
    {
        if (placeablePrefabs.Length == 0) return;
        if (currentPrefabIndex >= placeablePrefabs.Length) return;

        GameObject prefab = placeablePrefabs[currentPrefabIndex];
        if (!prefab) return;

        currentPreview = Instantiate(prefab);
        currentPreview.name = "BuildPreview";

        // Make it semi-transparent
        if (previewMaterial)
        {
            Renderer[] renderers = currentPreview.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.material = previewMaterial;
            }
        }

        // Disable colliders
        Collider[] colliders = currentPreview.GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }

        currentPreview.SetActive(false);
    }

    void UpdatePreview()
    {
        if (!currentPreview) return;
        if (!playerCamera) return;

        // Raycast from camera
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, placementRange, placementLayer))
        {
            currentPreview.SetActive(true);
            currentPreview.transform.position = hit.point;
        }
        else
        {
            currentPreview.SetActive(false);
        }
    }

    void TryPlaceObject()
    {
        if (!enabled) return;
        if (!currentPreview || !currentPreview.activeSelf) return;
        if (currentPrefabIndex >= placeablePrefabs.Length) return;

        GameObject prefab = placeablePrefabs[currentPrefabIndex];
        if (!prefab) return;

        // Get placement position
        Vector3 position = currentPreview.transform.position;
        Quaternion rotation = currentPreview.transform.rotation;

        // Request server to spawn object
        if (IsServer)
        {
            SpawnObjectOnNetwork(currentPrefabIndex, position, rotation);
        }
        else
        {
            RequestSpawnObjectServerRpc(currentPrefabIndex, position, rotation);
        }
    }

    [ServerRpc]
    void RequestSpawnObjectServerRpc(int prefabIndex, Vector3 position, Quaternion rotation)
    {
        SpawnObjectOnNetwork(prefabIndex, position, rotation);
    }

    void SpawnObjectOnNetwork(int prefabIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        if (prefabIndex >= placeablePrefabs.Length) return;

        GameObject prefab = placeablePrefabs[prefabIndex];
        if (!prefab) return;

        GameObject spawnedObject = Instantiate(prefab, position, rotation);

        // Add NetworkObject if it doesn't have one
        NetworkObject netObj = spawnedObject.GetComponent<NetworkObject>();
        if (!netObj)
        {
            netObj = spawnedObject.AddComponent<NetworkObject>();
        }

        // Spawn on network
        netObj.Spawn();

        // Track placed object
        placedObjects.Add(spawnedObject);

        Debug.Log($"[NetworkBuildSystem] Spawned {prefab.name} on network");
    }

    void TryRemoveObject()
    {
        if (!enabled) return;
        if (!playerCamera) return;

        // Raycast to find object to remove
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, placementRange))
        {
            GameObject hitObject = hit.collider.gameObject;
            
            // Check if it's a placed object
            if (placedObjects.Contains(hitObject))
            {
                // Request server to remove
                if (IsServer)
                {
                    RemoveObjectOnNetwork(hitObject);
                }
                else
                {
                    RequestRemoveObjectServerRpc(hitObject.GetComponent<NetworkObject>().NetworkObjectId);
                }
            }
        }
    }

    [ServerRpc]
    void RequestRemoveObjectServerRpc(ulong networkObjectId)
    {
        NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        if (netObj)
        {
            RemoveObjectOnNetwork(netObj.gameObject);
        }
    }

    void RemoveObjectOnNetwork(GameObject obj)
    {
        if (!IsServer) return;

        // Remove from tracking
        placedObjects.Remove(obj);

        // Get NetworkObject and despawn
        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        if (netObj)
        {
            netObj.Despawn();
        }
        else
        {
            Destroy(obj);
        }

        Debug.Log($"[NetworkBuildSystem] Removed object from network");
    }
}