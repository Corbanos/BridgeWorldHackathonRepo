using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LowPolyIsland : MonoBehaviour
{
    [Header("Shape")]
    [Range(6, 128)] public int segments = 32;
    public float height = 60f;
    public float topRadius = 35f;
    public float bottomRadius = 8f;
    [Tooltip("Irregular coastline amount (0 = perfect circle)")]
    [Range(0f, 0.5f)] public float rimNoise = 0.15f;
    public int seed = 1234;

    [Header("Materials (Top = Submesh 0, Sides = Submesh 1)")]
    public Material grassMat;
    public Material rockMat;

    [Header("Collider")]
    public bool addMeshCollider = true;

    MeshFilter mf; MeshRenderer mr; MeshCollider mc;

    void OnEnable() { Build(); }
    void OnValidate() { Build(); }

    void Build()
    {
        if (segments < 6) segments = 6;
        if (!mf) mf = GetComponent<MeshFilter>();
        if (!mr) mr = GetComponent<MeshRenderer>();

        var mesh = new Mesh();
        mesh.name = "LowPolyIsland";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // --- Generate rim (top & bottom) with noise ---
        var rnd = new System.Random(seed);
        float[] noise = new float[segments];
        for (int i = 0; i < segments; i++)
            noise[i] = 1f + ((float)rnd.NextDouble() * 2f - 1f) * rimNoise;

        // vertices
        Vector3[] topRing = new Vector3[segments];
        Vector3[] botRing = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            float nr = noise[i];
            float tx = Mathf.Cos(a) * topRadius * nr;
            float tz = Mathf.Sin(a) * topRadius * nr;
            float bx = Mathf.Cos(a) * bottomRadius * nr;
            float bz = Mathf.Sin(a) * bottomRadius * nr;
            topRing[i] = new Vector3(tx, height, tz);
            botRing[i] = new Vector3(bx, 0f, bz);
        }

        // assemble combined arrays
        // order: [top center][top ring][bottom ring]
        int vTopCenter = 0;
        int vTopStart = 1;
        int vBotStart = 1 + segments;

        Vector3[] verts = new Vector3[1 + segments + segments];
        Vector3[] norms = new Vector3[verts.Length];
        Vector2[] uvs   = new Vector2[verts.Length];

        verts[vTopCenter] = new Vector3(0f, height, 0f);
        uvs[vTopCenter] = new Vector2(0.5f, 0.5f);
        norms[vTopCenter] = Vector3.up;

        for (int i = 0; i < segments; i++)
        {
            int vt = vTopStart + i;
            int vb = vBotStart + i;
            verts[vt] = topRing[i];
            verts[vb] = botRing[i];

            // Planar UVs for top, cylindrical for sides (simple)
            Vector2 uvTop = new Vector2(
                (topRing[i].x / (topRadius * 2f)) + 0.5f,
                (topRing[i].z / (topRadius * 2f)) + 0.5f
            );
            uvs[vt] = uvTop;
            uvs[vb] = new Vector2(i / (float)segments, 0f);

            norms[vt] = Vector3.up;        // flat-shaded top
            norms[vb] = Vector3.down;      // not used for sides (we set hard normals via triangles)
        }

                // --- Triangles per submesh ---
        // Submesh 0: TOP fan (faces UP)
        int[] topTris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            int i0 = vTopCenter;
            int i1 = vTopStart + i;
            int i2 = vTopStart + ((i + 1) % segments);
            int t = i * 3;
            topTris[t] = i0; topTris[t + 1] = i2; topTris[t + 2] = i1; // flipped
        }

        // Submesh 1: SIDES (faces OUT)
        int[] sideTris = new int[segments * 6];
        int s = 0;
        for (int i = 0; i < segments; i++)
        {
            int iTopA = vTopStart + i;
            int iTopB = vTopStart + ((i + 1) % segments);
            int iBotA = vBotStart + i;
            int iBotB = vBotStart + ((i + 1) % segments);

            sideTris[s++] = iTopA; sideTris[s++] = iTopB; sideTris[s++] = iBotA;
            sideTris[s++] = iTopB; sideTris[s++] = iBotB; sideTris[s++] = iBotA;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(topTris, 0);
        mesh.SetTriangles(sideTris, 1);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();   // <- add this

        // Optional: leave normals as-is for faceted look on top. For extra-faceted sides,
        // you can duplicate per-face verts, but this is already “low-poly enough”.

        mf.sharedMesh = mesh;

        // materials
        if (grassMat && rockMat) mr.sharedMaterials = new[] { grassMat, rockMat };
        else if (grassMat)       mr.sharedMaterials = new[] { grassMat, grassMat };
        else if (rockMat)        mr.sharedMaterials = new[] { rockMat, rockMat };

        // collider
        if (addMeshCollider)
        {
            mc = GetComponent<MeshCollider>();
            if (!mc) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }
    }
}
