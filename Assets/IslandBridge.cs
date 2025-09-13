using UnityEngine;
using Unity.Netcode;

public class IslandBridge : NetworkBehaviour
{
    [Header("Bridge Settings")]
    public Transform startPoint;
    public Transform endPoint;
    public GameObject bridgeSegmentPrefab;
    public float segmentLength = 2f;
    public bool autoConnect = true;
    
    [Header("Visual")]
    public Material activeBridgeMaterial;
    public Material inactiveBridgeMaterial;
    
    // Network variables
    private NetworkVariable<bool> isActive = new NetworkVariable<bool>(false);
    
    // Bridge segments
    private GameObject[] bridgeSegments;
    private bool isBuilt = false;
    
    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes
        isActive.OnValueChanged += OnBridgeActiveChanged;
        
        if (autoConnect && IsServer)
        {
            // Activate bridge when spawned
            isActive.Value = true;
        }
    }
    
    public override void OnNetworkDespawn()
    {
        isActive.OnValueChanged -= OnBridgeActiveChanged;
    }
    
    void OnBridgeActiveChanged(bool previousValue, bool newValue)
    {
        if (newValue && !isBuilt)
        {
            BuildBridge();
        }
        else if (!newValue && isBuilt)
        {
            DestroyBridge();
        }
        
        UpdateBridgeVisuals();
    }
    
    void BuildBridge()
    {
        if (startPoint == null || endPoint == null || bridgeSegmentPrefab == null)
        {
            Debug.LogWarning("Bridge missing required components!");
            return;
        }
        
        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;
        float distance = Vector3.Distance(start, end);
        
        int segmentCount = Mathf.CeilToInt(distance / segmentLength);
        bridgeSegments = new GameObject[segmentCount];
        
        for (int i = 0; i < segmentCount; i++)
        {
            float t = (float)i / (segmentCount - 1);
            Vector3 position = Vector3.Lerp(start, end, t);
            Quaternion rotation = Quaternion.LookRotation(end - start);
            
            GameObject segment = Instantiate(bridgeSegmentPrefab, position, rotation, transform);
            bridgeSegments[i] = segment;
            
            // Scale segment if needed
            float actualSegmentLength = (i == segmentCount - 1) ? distance - (i * segmentLength) : segmentLength;
            segment.transform.localScale = new Vector3(1f, 1f, actualSegmentLength / bridgeSegmentPrefab.transform.localScale.z);
        }
        
        isBuilt = true;
        UpdateBridgeVisuals();
        
        Debug.Log($"Built bridge with {segmentCount} segments over {distance:F1} units");
    }
    
    void DestroyBridge()
    {
        if (bridgeSegments != null)
        {
            foreach (var segment in bridgeSegments)
            {
                if (segment != null)
                {
                    if (Application.isPlaying)
                        Destroy(segment);
                    else
                        DestroyImmediate(segment);
                }
            }
            bridgeSegments = null;
        }
        
        isBuilt = false;
        Debug.Log("Destroyed bridge");
    }
    
    void UpdateBridgeVisuals()
    {
        if (bridgeSegments == null) return;
        
        Material materialToUse = isActive.Value ? activeBridgeMaterial : inactiveBridgeMaterial;
        
        foreach (var segment in bridgeSegments)
        {
            if (segment != null)
            {
                var renderer = segment.GetComponent<Renderer>();
                if (renderer != null && materialToUse != null)
                {
                    renderer.material = materialToUse;
                }
                
                // Enable/disable collider based on active state
                var collider = segment.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = isActive.Value;
                }
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void ToggleBridgeServerRpc()
    {
        isActive.Value = !isActive.Value;
    }
    
    // Called when player walks onto bridge
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player entered bridge to island");
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player left bridge");
        }
    }
    
    // Visual debugging
    void OnDrawGizmos()
    {
        if (startPoint != null && endPoint != null)
        {
            Gizmos.color = isActive.Value ? Color.green : Color.red;
            Gizmos.DrawLine(startPoint.position, endPoint.position);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(startPoint.position, 0.5f);
            Gizmos.DrawWireSphere(endPoint.position, 0.5f);
        }
    }
}