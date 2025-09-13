using System.Collections.Generic;
using UnityEngine;

/// Auto-scatter flowers/grass/mushrooms/rocks/trees on top of a LowPolyIsland.
/// Robust to rotation/non-uniform scale. Rays only against this island's collider.
/// v2.1: exclude top-center vertex, better ring build, slope vs transform.up, optional debug logs
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(LowPolyIsland))]
public class IslandAutoDecorator : MonoBehaviour
{
    [Header("Prefabs (drag variants here)")]
    public List<GameObject> FlowerPrefabs;    // Flower1..4
    public List<GameObject> GrassPrefabs;     // Grass1..4
    public List<GameObject> MushroomPrefabs;  // Mushroom1..4
    public List<GameObject> RockPrefabs;      // Rock1..9
    public List<GameObject> TreePrefabs;      // Tree1..3

    [Header("Counts (density)")]
    public int flowerCount   = 60;
    public int grassCount    = 120;
    public int mushroomCount = 35;
    public int rockCount     = 20;
    public int treeCount     = 14;

    [Header("Placement Rules")]
    [Tooltip("Keep items this far away from the cliff edge (meters, in island local space).")]
    public float edgeMargin = 2.0f;

    [Tooltip("Max ground slope (deg) to allow placement on the top.")]
    [Range(0f, 60f)] public float maxSlope = 18f;

    [Tooltip("Attempts per object to find a valid spot before giving up.")]
    public int triesPerObject = 20;

    [Header("Spacing (avoid overlaps)")]
    public float treeMinSpacing   = 3.0f;
    public float rockMinSpacing   = 1.8f;
    public float plantMinSpacing  = 0.9f; // flowers/grass/mushrooms

    [Header("Scale ranges")]
    public Vector2 treeScale      = new Vector2(0.9f, 1.3f);
    public Vector2 rockScale      = new Vector2(0.9f, 1.4f);
    public Vector2 flowerScale    = new Vector2(0.8f, 1.2f);
    public Vector2 grassScale     = new Vector2(0.9f, 1.3f);
    public Vector2 mushroomScale  = new Vector2(0.8f, 1.2f);

    [Header("Offsets & Layers")]
    [Tooltip("Lift placed items slightly to avoid z-fighting.")]
    public float yOffset = 0.02f;

    [Tooltip("Layer(s) to raycast against. If left empty, auto-uses this GameObject's layer.")]
    public LayerMask surfaceMask = 0;

    [Header("Random Seed")]
    public int seed = 12345;

    [Header("Debug")]
    public bool debugLogs = false;

    // Internals
    private LowPolyIsland island;
    private MeshFilter meshFilter;
    private MeshCollider islandCollider;

    // Top polygon data (local space)
    private List<Vector3> topRingLocal = new(); // y ~= island.height, rim only
    private Vector3 topCenterLocal;
    private struct Tri { public int a, b; public float area; }
    private List<Tri> fan = new();  // triangle fan indices into topRingLocal
    private float totalArea;
    private bool usingFallbackDisk = false;

    private Transform rootFlowers, rootGrass, rootMushrooms, rootRocks, rootTrees;

    void Awake()
    {
        island = GetComponent<LowPolyIsland>();
        meshFilter = GetComponent<MeshFilter>();

        islandCollider = GetComponent<MeshCollider>();
        if (!islandCollider) islandCollider = gameObject.AddComponent<MeshCollider>();

        if (surfaceMask.value == 0)
            surfaceMask = 1 << gameObject.layer; // default: only this island's layer
    }

    void Start()
    {
        // ensure collider is current
        if (meshFilter && meshFilter.sharedMesh)
            islandCollider.sharedMesh = meshFilter.sharedMesh;

        BuildTopPolygon();
        ClearOld();
        Decorate();
    }

#if UNITY_EDITOR
    [ContextMenu("Regenerate Decoration")]
    public void RegenerateInEditor()
    {
        if (meshFilter && meshFilter.sharedMesh)
            islandCollider.sharedMesh = meshFilter.sharedMesh;
        BuildTopPolygon();
        ClearOld();
        Decorate();
    }
#endif

    // ------------------------- CORE: build top polygon -------------------------
    private void BuildTopPolygon()
    {
        topRingLocal.Clear();
        fan.Clear();
        totalArea = 0f;
        usingFallbackDisk = false;

        var mesh = meshFilter ? meshFilter.sharedMesh : null;
        if (!mesh || mesh.vertexCount == 0) return;

        // Find highest Y in local space
        var verts = mesh.vertices;
        float maxY = float.MinValue;
        for (int i = 0; i < verts.Length; i++)
            if (verts[i].y > maxY) maxY = verts[i].y;

        // Collect local verts on the top plane
        float eps = 0.0005f * Mathf.Max(1f, island.topRadius);
        var raw = new List<Vector3>();
        for (int i = 0; i < verts.Length; i++)
            if (Mathf.Abs(verts[i].y - maxY) <= eps)
                raw.Add(verts[i]);

        if (raw.Count < 3)
        {
            if (debugLogs) Debug.LogWarning("[IslandAutoDecorator] No top-plane verts found.");
            FallbackDisk();
            return;
        }

        // Deduplicate by XZ (keep rim points)
        var unique = new List<Vector3>();
        float mergeDist2 = 1e-6f; // tiny; local units
        foreach (var v in raw)
        {
            bool found = false;
            for (int j = 0; j < unique.Count; j++)
            {
                Vector3 d = v - unique[j];
                d.y = 0;
                if (d.sqrMagnitude <= mergeDist2) { found = true; break; }
            }
            if (!found) unique.Add(v);
        }

        // Compute center
        Vector3 c = Vector3.zero;
        foreach (var v in unique) c += v;
        c /= unique.Count;

        // Remove the top-center vertex (it exists in LowPolyIsland)
        // Any point very close to center in XZ is not part of the rim.
        float minRad = Mathf.Max(0.05f * island.topRadius, 0.1f); // 5% of radius or 0.1m
        for (int i = unique.Count - 1; i >= 0; i--)
        {
            Vector2 r = new Vector2(unique[i].x - c.x, unique[i].z - c.z);
            if (r.magnitude < minRad) unique.RemoveAt(i);
        }

        if (unique.Count < 3)
        {
            if (debugLogs) Debug.LogWarning("[IslandAutoDecorator] Rim collapsed after center removal — using disk fallback.");
            FallbackDisk();
            return;
        }

        // Sort by angle around center
        unique.Sort((p, q) =>
        {
            float ap = Mathf.Atan2(p.z - c.z, p.x - c.x);
            float aq = Mathf.Atan2(q.z - c.z, q.x - c.x);
            return ap.CompareTo(aq);
        });

        // Inset inward by edgeMargin
        float edge = Mathf.Max(0f, edgeMargin);
        topRingLocal.Clear();
        for (int i = 0; i < unique.Count; i++)
        {
            Vector3 v = unique[i];
            Vector3 radial = v - c;
            float len = new Vector2(radial.x, radial.z).magnitude;
            if (len > 1e-4f && edge > 0f)
            {
                float newLen = Mathf.Max(0f, len - edge);
                float k = newLen / len;
                v = new Vector3(c.x + radial.x * k, v.y, c.z + radial.z * k);
            }
            topRingLocal.Add(v);
        }

        topCenterLocal = c;

        // Triangle fan for area-weighted sampling (in XZ)
        totalArea = 0f;
        fan.Clear();
        for (int i = 0; i < topRingLocal.Count; i++)
        {
            int j = (i + 1) % topRingLocal.Count;
            Vector2 a = new Vector2(topRingLocal[i].x - c.x, topRingLocal[i].z - c.z);
            Vector2 b = new Vector2(topRingLocal[j].x - c.x, topRingLocal[j].z - c.z);
            float area = Mathf.Abs(a.x * b.y - a.y * b.x) * 0.5f;
            if (area > 1e-8f)
            {
                fan.Add(new Tri { a = i, b = j, area = area });
                totalArea += area;
            }
        }

        if (fan.Count == 0)
        {
            if (debugLogs) Debug.LogWarning("[IslandAutoDecorator] Degenerate fan — using disk fallback.");
            FallbackDisk();
        }
        else if (debugLogs)
        {
            Debug.Log($"[IslandAutoDecorator] Top rim verts: {topRingLocal.Count}, fan tris: {fan.Count}, area: {totalArea:F3}");
        }
    }

    private void FallbackDisk()
    {
        // If we can’t build the rim, sample a disk using topRadius - edgeMargin.
        usingFallbackDisk = true;
        topRingLocal.Clear();
        fan.Clear();
        totalArea = 0f;
        topCenterLocal = new Vector3(0f, island.height, 0f);
    }

    // Sample a random LOCAL point on the top polygon or fallback disk
    private Vector3 SampleLocalOnTop(System.Random rng)
    {
        if (usingFallbackDisk)
        {
            float R = Mathf.Max(0.1f, island.topRadius - edgeMargin);
            float r = Mathf.Sqrt((float)rng.NextDouble()) * R;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            return topCenterLocal + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        // pick triangle by area
        double pick = rng.NextDouble() * totalArea;
        float accum = 0f;
        int idx = 0;
        for (; idx < fan.Count; idx++)
        {
            accum += fan[idx].area;
            if (pick <= accum) break;
        }
        if (idx >= fan.Count) idx = fan.Count - 1;
        var tri = fan[idx];

        Vector3 c = topCenterLocal;
        Vector3 A = topRingLocal[tri.a];
        Vector3 B = topRingLocal[tri.b];

        // random barycentric (uniform)
        float r1 = (float)rng.NextDouble();
        float r2 = (float)rng.NextDouble();
        float su = Mathf.Sqrt(r1);
        float u = 1 - su;
        float v = su * (1 - r2);
        float w = su * r2;

        return u * c + v * A + w * B; // on top plane (local)
    }

    // ---------------------------- utility / housekeeping ----------------------------
    private void ClearOld()
    {
        DestroyIfExists("_AUTO_TREES");
        DestroyIfExists("_AUTO_ROCKS");
        DestroyIfExists("_AUTO_FLOWERS");
        DestroyIfExists("_AUTO_GRASS");
        DestroyIfExists("_AUTO_MUSHROOMS");

        rootTrees     = new GameObject("_AUTO_TREES").transform;     rootTrees.parent = transform;
        rootRocks     = new GameObject("_AUTO_ROCKS").transform;     rootRocks.parent = transform;
        rootFlowers   = new GameObject("_AUTO_FLOWERS").transform;   rootFlowers.parent = transform;
        rootGrass     = new GameObject("_AUTO_GRASS").transform;     rootGrass.parent = transform;
        rootMushrooms = new GameObject("_AUTO_MUSHROOMS").transform; rootMushrooms.parent = transform;
    }

    private void DestroyIfExists(string name)
    {
        var t = transform.Find(name);
        if (t) DestroyImmediate(t.gameObject);
    }

    private void Decorate()
    {
        if (!meshFilter || !meshFilter.sharedMesh)
        {
            if (debugLogs) Debug.LogWarning("[IslandAutoDecorator] No mesh to decorate.");
            return;
        }

        // make sure collider is synced
        islandCollider.sharedMesh = meshFilter.sharedMesh;

        var rng = new System.Random(seed);

        var placedTreePositions  = new List<Vector3>();
        var placedRockPositions  = new List<Vector3>();
        var placedPlantPositions = new List<Vector3>();

        int before = TotalChildren();

        ScatterCategory(rng, TreePrefabs,  treeCount,  rootTrees,     treeMinSpacing,  treeScale,     alignToNormal:false, spacingBucket: placedTreePositions);
        ScatterCategory(rng, RockPrefabs,  rockCount,  rootRocks,     rockMinSpacing,  rockScale,     alignToNormal:true,  spacingBucket: placedRockPositions);
        ScatterCategory(rng, FlowerPrefabs,flowerCount,rootFlowers,   plantMinSpacing, flowerScale,   alignToNormal:false, spacingBucket: placedPlantPositions);
        ScatterCategory(rng, GrassPrefabs, grassCount, rootGrass,     plantMinSpacing, grassScale,    alignToNormal:false, spacingBucket: placedPlantPositions);
        ScatterCategory(rng, MushroomPrefabs, mushroomCount, rootMushrooms, plantMinSpacing, mushroomScale, false, placedPlantPositions);

        int after = TotalChildren();

        if (debugLogs) Debug.Log($"[IslandAutoDecorator] Placed {(after - before)} instances (Trees:{rootTrees.childCount}, Rocks:{rootRocks.childCount}, Plants:{rootFlowers.childCount + rootGrass.childCount + rootMushrooms.childCount}).");
    }

    private int TotalChildren()
    {
        int n = 0;
        if (rootTrees) n += rootTrees.childCount;
        if (rootRocks) n += rootRocks.childCount;
        if (rootFlowers) n += rootFlowers.childCount;
        if (rootGrass) n += rootGrass.childCount;
        if (rootMushrooms) n += rootMushrooms.childCount;
        return n;
    }

    private void ScatterCategory(
        System.Random rng,
        List<GameObject> prefabs, int count, Transform parent,
        float minSpacing, Vector2 scaleRange, bool alignToNormal,
        List<Vector3> spacingBucket)
    {
        if (prefabs == null || prefabs.Count == 0 || count <= 0) return;

        int placed = 0;
        int safety = Mathf.Max(1, triesPerObject) * count;

        while (placed < count && safety-- > 0)
        {
            Vector3 localTop = SampleLocalOnTop(rng);

            Vector3 worldTop = transform.TransformPoint(localTop);
            Vector3 up = transform.up;
            Vector3 rayStart = worldTop + up * 0.75f;
            Vector3 rayDir   = -up;

            Vector3 pos = worldTop + up * yOffset;
            Vector3 normal = up;

            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, 2.0f, surfaceMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != islandCollider) continue; // only our island
                normal = hit.normal;

                // Slope vs island's up, not world up
                float slope = Vector3.Angle(normal, up);
                if (slope > maxSlope) continue;

                pos = hit.point + up * yOffset;
            }

            // Spacing
            bool tooClose = false;
            for (int i = 0; i < spacingBucket.Count; i++)
            {
                if ((spacingBucket[i] - pos).sqrMagnitude < (minSpacing * minSpacing))
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            // Spawn
            var prefab = prefabs[rng.Next(prefabs.Count)];
            if (!prefab) continue;

            Quaternion rot = alignToNormal
                ? Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)
                : Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());

            var go = Instantiate(prefab, pos, rot, parent);
            go.transform.localScale *= s;

            spacingBucket.Add(pos);
            placed++;
        }
    }

    // ---------------------------- gizmos ----------------------------
    void OnDrawGizmosSelected()
    {
        // Draw the sampled rim (or disk)
        Gizmos.color = usingFallbackDisk ? new Color(1f, 0.6f, 0f) : Color.green;

        if (!usingFallbackDisk && topRingLocal != null && topRingLocal.Count >= 3)
        {
            Vector3 prev = transform.TransformPoint(topRingLocal[0]);
            for (int i = 1; i <= topRingLocal.Count; i++)
            {
                Vector3 cur = transform.TransformPoint(topRingLocal[i % topRingLocal.Count]);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
        else
        {
            // show fallback disk radius
            if (island != null)
            {
                float R = Mathf.Max(0.1f, island.topRadius - edgeMargin);
                Vector3 c = transform.TransformPoint(new Vector3(0f, island.height, 0f));
                const int steps = 64;
                Vector3 prev = c + transform.right * R;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (i / (float)steps) * Mathf.PI * 2f;
                    Vector3 dir = (transform.right * Mathf.Cos(t) + transform.forward * Mathf.Sin(t)).normalized;
                    Vector3 cur = c + dir * R;
                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
        }

        // center
        if (island != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.TransformPoint(topCenterLocal), 0.15f);
        }
    }
}
