using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BuilderTool : MonoBehaviour
{
    [Header("Scene References")]
    public Camera cam;
    public Transform buildRoot;                 // auto-created if missing
    public Transform islandRoot;                // assign your island root
    public Collider islandCollider;             // assign island MeshCollider
    public LayerMask surfaceMask;               // island layer; leave 0 to hit everything

    [Header("Player Control Freeze")]
    public Transform playerRoot;
    public Rigidbody playerRigidbody;
    public CharacterController playerCC;
    public PlayerInput playerInput;
    public string gameplayActionMap = "Player";
    public string uiActionMap = "UI";
    public MonoBehaviour[] disableWhileBuilding;
    public string[] extraScriptNamesToDisable = new[] { "PlayerController" };

    [Header("Catalog")]
    public List<PlaceableSO> catalog = new();

    [Header("Placement Rules")]
    [Range(0f, 60f)] public float maxSlope = 25f;
    public bool gridSnap = false;
    public float gridSize = 1f;
    public float maxRayDistance = 400f;
    [Tooltip("Also treat IslandAutoDecorator meshes as valid surface hits.")]
    public bool acceptDecoratorSurfaces = true;
    public string[] acceptedSurfaceNameContains = new[] { "IslandAutoDecorator" };
    public string[] acceptedSurfaceTags = new string[0];

    [Header("Ghost")]
    public Material ghostMaterial;
    public Color ghostTint = new(0.7f, 1f, 0.7f, 0.5f);

    [Header("Build Camera Mode")]
    public bool useOrthographic = true;
    public float orthoSizeMultiplier = 1.3f;
    public float birdHeight = 60f;
    public float birdPitchDeg = 65f;
    public float birdRadiusPadding = 6f;

    [Header("Build Camera Controls")]
    public float panSpeed = 25f;
    public float panSpeedShift = 45f;
    public float zoomSpeed = 10f;
    public float minOrthoSize = 6f;
    public float maxOrthoSize = 140f;
    public float minHeight = 12f;
    public float maxHeight = 220f;

    [Header("Save/Load")]
    public bool autoLoadOnStart = true;
    public bool autoSaveOnPlaceDelete = true;
    public bool saveOnExit = true;
    public bool useIslandNameForSaveFile = true;
    public string saveFile = "island_build_default.json";

    [Header("Input/Debug")]
    public Key toggleBuildKey = Key.B;
    public Key toggleCatalogKey = Key.Tab;
    public bool debugLogs = false;

    [Header("Raycast Options / Debug")]
    public bool allowAnySurface = false;         // TEMP: accept ANY collider hit as valid
    public bool includeTriggerColliders = true;  // Raycast hits triggers too
    public bool showHitGizmo = true;             // Draw a gizmo where the ray hits
    public bool logRayMissReasons = true;        // Verbose logs for misses


    // ── runtime
    bool buildMode = false;
    int index = 0; float yaw = 0f;
    GameObject ghost; Collider[] ghostCols;
    Keyboard kb; Mouse mouse;
    bool suppressClickUntilRelease = false;
    static bool isQuitting = false;

    // camera cache
    Transform camPrevParent;
    Vector3 camLocalPosPrev; Quaternion camLocalRotPrev;
    bool camOrthoPrev; float camSizePrev; float camFovPrev;
    CursorLockMode prevLock; bool prevVisible;

    // player caches
    bool rbHad, rbPrevKinematic, rbPrevUseGravity; RigidbodyConstraints rbPrevConstraints; Vector3 rbPrevVel;
    bool ccHad, ccPrevEnabled;
    Behaviour cineBrain; bool cineBrainPrev;
    readonly List<MonoBehaviour> autoDisabled = new();

    [Serializable] class SaveData { public List<Entry> items = new(); public int version = 1; }
    [Serializable] class Entry { public string id; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
    string EffectiveSaveFile => useIslandNameForSaveFile && islandRoot ? $"island_build_{islandRoot.name}.json" : saveFile;
    string SavePath => System.IO.Path.Combine(Application.persistentDataPath, EffectiveSaveFile);

    // ──────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        kb = Keyboard.current; mouse = Mouse.current;
        if (!cam) cam = Camera.main;

        if (!buildRoot)
        {
            var go = GameObject.Find("_BUILD_ROOT");
            if (!go) go = new GameObject("_BUILD_ROOT");
            buildRoot = go.transform;
        }

        if (!islandRoot || !islandCollider)
        {
            var lp = FindObjectOfType<LowPolyIsland>();
            if (lp)
            {
                if (!islandRoot) islandRoot = lp.transform;
                if (!islandCollider) islandCollider = lp.GetComponent<MeshCollider>();
            }
        }

        if (surfaceMask.value == 0 && islandCollider)
            surfaceMask = 1 << islandCollider.gameObject.layer;

        if (!playerInput) playerInput = FindObjectOfType<PlayerInput>();
        if (!playerRigidbody && playerRoot) playerRigidbody = playerRoot.GetComponent<Rigidbody>();
        if (!playerCC && playerRoot) playerCC = playerRoot.GetComponent<CharacterController>();

        if (autoLoadOnStart && System.IO.File.Exists(SavePath)) SafeLoad();
        buildMode = false;
    }

    void OnApplicationQuit()
    {
        isQuitting = true;
        if (saveOnExit) SafeSaveSilently();
    }

    void OnDisable()
    {
        // Skip auto-save if quitting or not in play, or if buildRoot is gone
        if (saveOnExit && Application.isPlaying && !isQuitting) SafeSaveSilently();
    }

    void Update()
    {
        if (kb == null || mouse == null) return;

        if (kb[toggleBuildKey].wasPressedThisFrame)
        {
            if (buildMode) ExitBuildMode(); else EnterBuildMode();
        }
        if (!buildMode) return;

        if (kb[toggleCatalogKey].wasPressedThisFrame)
            BuilderCatalogUI.ToggleGlobalPanel();

        float wheel = mouse.scroll.ReadValue().y;
        if (wheel > 0.02f) NextItem(1);
        else if (wheel < -0.02f) NextItem(-1);
        for (int n = 1; n <= 9 && n <= catalog.Count; n++)
            if (kb[(Key)((int)Key.Digit1 + n - 1)].wasPressedThisFrame) SetIndex(n - 1);

        if (kb.qKey.wasPressedThisFrame) yaw -= Current.rotateStepDegrees;
        if (kb.eKey.wasPressedThisFrame) yaw += Current.rotateStepDegrees;
        if (kb.gKey.wasPressedThisFrame) gridSnap = !gridSnap;

        if (kb.f5Key.wasPressedThisFrame) SafeSave();
        if (kb.f9Key.wasPressedThisFrame) SafeLoad();

        HandleBuildCameraControls();
        UpdateGhost();

        if (suppressClickUntilRelease)
        {
            if (mouse.leftButton.wasReleasedThisFrame) suppressClickUntilRelease = false;
        }
        else
        {
            if (!PointerOverUI() && mouse.leftButton.wasPressedThisFrame) TryPlace();
            if (!PointerOverUI() && mouse.rightButton.wasPressedThisFrame) TryDelete();
        }

        bool altDown = kb[Key.LeftAlt].isPressed || kb[Key.RightAlt].isPressed;
        if (!PointerOverUI() && altDown && mouse.leftButton.wasPressedThisFrame) TryPick();
    }

    // ── build mode
    public void EnterBuildMode()
    {
        if (buildMode) return;
        buildMode = true;

        SwitchActionMap(true);
        foreach (var s in disableWhileBuilding) if (s) s.enabled = false;
        DisableScriptsByName();

        cineBrain = cam ? cam.GetComponent("CinemachineBrain") as Behaviour : null;
        if (cineBrain) { cineBrainPrev = cineBrain.enabled; cineBrain.enabled = false; }

        rbHad = playerRigidbody;
        if (rbHad)
        {
            rbPrevKinematic = playerRigidbody.isKinematic;
            rbPrevUseGravity = playerRigidbody.useGravity;
            rbPrevConstraints = playerRigidbody.constraints;
            rbPrevVel = playerRigidbody.linearVelocity;
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
            playerRigidbody.useGravity = false;
            playerRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
        ccHad = playerCC;
        if (ccHad) { ccPrevEnabled = playerCC.enabled; playerCC.enabled = false; }

        if (cam)
        {
            camPrevParent = cam.transform.parent;
            camLocalPosPrev = cam.transform.localPosition;
            camLocalRotPrev = cam.transform.localRotation;
            camOrthoPrev = cam.orthographic;
            camSizePrev = cam.orthographicSize;
            camFovPrev = cam.fieldOfView;
            cam.transform.SetParent(null, true);
        }

        prevLock = Cursor.lockState; prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        MoveCameraToBuildView();
        SpawnGhost(); if (ghost) ghost.SetActive(true);

        if (debugLogs) Debug.Log("[Builder] Entered build mode");
    }

    public void ExitBuildMode()
    {
        if (!buildMode) return;
        buildMode = false;

        if (cam)
        {
            cam.transform.SetParent(camPrevParent, false);
            cam.transform.localPosition = camLocalPosPrev;
            cam.transform.localRotation = camLocalRotPrev;
            cam.orthographic = camOrthoPrev;
            cam.orthographicSize = camSizePrev;
            cam.fieldOfView = camFovPrev;
        }
        if (cineBrain) cineBrain.enabled = cineBrainPrev;

        if (rbHad)
        {
            playerRigidbody.constraints = rbPrevConstraints;
            playerRigidbody.isKinematic = rbPrevKinematic;
            playerRigidbody.useGravity = rbPrevUseGravity;
            playerRigidbody.linearVelocity = rbPrevVel;
        }
        if (ccHad) playerCC.enabled = ccPrevEnabled;

        ReenableScriptsByName();
        foreach (var s in disableWhileBuilding) if (s) s.enabled = true;
        SwitchActionMap(false);

        Cursor.lockState = prevLock; Cursor.visible = prevVisible;
        if (ghost) ghost.SetActive(false);

        if (debugLogs) Debug.Log("[Builder] Exited build mode");
    }

    void SwitchActionMap(bool toUI)
    {
        if (!playerInput) return;
        try
        {
            string target = toUI ? uiActionMap : gameplayActionMap;
            if (!string.IsNullOrEmpty(target) && playerInput.currentActionMap?.name != target)
                playerInput.SwitchCurrentActionMap(target);
        }
        catch { }
    }

    // ── camera
    void MoveCameraToBuildView()
    {
        if (!cam) return;

        float radius = 20f;
        var low = islandRoot ? islandRoot.GetComponent<LowPolyIsland>() : null;
        if (low) radius = Mathf.Max(6f, low.topRadius + birdRadiusPadding);

        Vector3 up = islandRoot ? islandRoot.up : Vector3.up;
        Vector3 fwdRef = islandRoot ? islandRoot.forward : Vector3.forward;
        Vector3 topCenter = islandRoot
            ? islandRoot.TransformPoint(0f, low ? low.height : 0f, 0f)
            : cam.transform.position + cam.transform.forward * 10f;

        if (useOrthographic)
        {
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Clamp(radius * orthoSizeMultiplier, minOrthoSize, maxOrthoSize);
            cam.transform.position = topCenter + up * 1000f;
            cam.transform.rotation = Quaternion.LookRotation(-up, fwdRef);
        }
        else
        {
            cam.orthographic = false;
            Quaternion yawQ = Quaternion.LookRotation(fwdRef, up);
            Quaternion pitchQ = Quaternion.AngleAxis(-birdPitchDeg, Vector3.right);
            cam.transform.rotation = yawQ * pitchQ;
            cam.transform.position = topCenter + (cam.transform.rotation * Vector3.back) * (radius * 2f) + up * birdHeight;
            cam.fieldOfView = 60f;
            float h = Vector3.Dot(cam.transform.position - topCenter, up);
            h = Mathf.Clamp(h, minHeight, maxHeight);
            Vector3 planar = Vector3.ProjectOnPlane(cam.transform.position - topCenter, up);
            cam.transform.position = topCenter + planar + up * h;
        }
    }

    void HandleBuildCameraControls()
    {
        if (!cam) return;
        Vector3 up = islandRoot ? islandRoot.up : Vector3.up;
        Vector3 topCenter = islandRoot ? islandRoot.position : Vector3.zero;

        float spd = (kb.shiftKey.isPressed ? panSpeedShift : panSpeed) * Time.deltaTime;
        Vector2 move = Vector2.zero;
        if (kb.wKey.isPressed) move.y += 1f;
        if (kb.sKey.isPressed) move.y -= 1f;
        if (kb.aKey.isPressed) move.x -= 1f;
        if (kb.dKey.isPressed) move.x += 1f;
        if (move.sqrMagnitude > 1f) move.Normalize();

        Vector3 rightOnPlane, fwdOnPlane;
        if (useOrthographic)
        {
            Vector3 refFwd = islandRoot ? islandRoot.forward : Vector3.forward;
            rightOnPlane = Vector3.Normalize(Vector3.Cross(up, refFwd));
            fwdOnPlane   = Vector3.Normalize(Vector3.Cross(rightOnPlane, up));
        }
        else
        {
            fwdOnPlane   = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            if (fwdOnPlane.sqrMagnitude < 0.0001f) fwdOnPlane = Vector3.Cross(up, cam.transform.right).normalized;
            rightOnPlane = Vector3.Cross(up, fwdOnPlane).normalized;
        }
        cam.transform.position += (rightOnPlane * move.x + fwdOnPlane * move.y) * spd;

        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            if (useOrthographic)
            {
                float size = cam.orthographicSize - wheel * (zoomSpeed * 0.5f);
                cam.orthographicSize = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
            }
            else
            {
                float dolly = wheel * zoomSpeed;
                cam.transform.position += cam.transform.forward * dolly;

                float hMin = minHeight, hMax = maxHeight;
                Vector3 islandTop = islandRoot ? islandRoot.TransformPoint(0f, (FindFirstObjectByType<LowPolyIsland>()?.height ?? 0f), 0f) : topCenter;
                float h = Vector3.Dot(cam.transform.position - islandTop, up);
                h = Mathf.Clamp(h, hMin, hMax);
                Vector3 planar = Vector3.ProjectOnPlane(cam.transform.position - islandTop, up);
                cam.transform.position = islandTop + planar + up * h;
            }
        }
    }

    // ── catalog / ghost
    PlaceableSO Current => (index >= 0 && index < catalog.Count) ? catalog[index] : null;

    public void SelectFromUI(int indexToSelect)
    {
        SetIndex(indexToSelect);
        suppressClickUntilRelease = true; // don’t place on the same click that closed the UI
    }

    public void SetIndex(int i)
    {
        index = Mathf.Clamp(i, 0, Mathf.Max(0, catalog.Count - 1));
        yaw = 0f;
        SpawnGhost();
    }

    public void NextItem(int d)
    {
        if (catalog.Count == 0) return;
        index = (index + d + catalog.Count) % catalog.Count;
        yaw = 0f; SpawnGhost();
    }

    void SpawnGhost()
    {
        if (ghost) Destroy(ghost);
        if (Current == null || Current.prefab == null) return;

        ghost = Instantiate(Current.prefab);
        ghost.name = "[GHOST] " + Current.id;

        if (ghostMaterial)
        {
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                r.sharedMaterials = mats;
            }
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ghostTint);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", ghostTint);
            }
        }
        ghostCols = ghost.GetComponentsInChildren<Collider>(true);
        foreach (var c in ghostCols) c.enabled = false;
    }

    void UpdateGhost()
    {
        if (!ghost) { SpawnGhost(); if (!ghost) return; }
        if (!cam) { ghost.SetActive(false); return; }

        if (!RayToIsland(out var hit))
        {
            if (debugLogs) Debug.Log("[Builder] Ghost hidden: ray didn’t hit island/decorator.");
            ghost.SetActive(false); return;
        }

        Vector3 up = hit.normal;
        float slope = Vector3.Angle(up, islandRoot ? islandRoot.up : Vector3.up);
        if (slope > maxSlope)
        {
            if (debugLogs) Debug.Log($"[Builder] Ghost hidden: slope {slope:F1} > max {maxSlope}");
            ghost.SetActive(false); return;
        }

        Vector3 pos = hit.point;
        if (gridSnap) pos = SnapXZ(pos, gridSize);
        pos += up * Current.yOffset;

        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        if (Current.alignToSurfaceNormal)
            rot = Quaternion.FromToRotation(Vector3.up, up) * rot;

        ghost.transform.SetPositionAndRotation(pos, rot);
        ghost.SetActive(true);
    }

    void TryPlace()
    {
        if (!buildMode) return;
        if (Current == null || Current.prefab == null) return;
        if (!ghost || !ghost.activeSelf) return;
        if (IsTooClose(ghost.transform.position, Current.footprintRadius)) return;

        var go = Instantiate(Current.prefab, ghost.transform.position, ghost.transform.rotation, buildRoot);
        go.name = Current.id + " (Placed)";
        if (autoSaveOnPlaceDelete) SafeSaveSilently();
    }

    void TryDelete()
    {
        if (!buildMode) return;
        if (!RayFromPointer(out var hit)) return;
        Transform t = hit.transform;
        while (t && t.parent != buildRoot) t = t.parent;
        if (t && t.parent == buildRoot)
        {
            Destroy(t.gameObject);
            if (autoSaveOnPlaceDelete) SafeSaveSilently();
        }
    }

    void TryPick()
    {
        if (!buildMode) return;
        if (!RayFromPointer(out var hit)) return;
        Transform root = hit.transform;
        while (root && root.parent != buildRoot) root = root.parent;
        if (!root) return;

        string baseName = root.name.Replace("(Placed)", "").Replace("(Clone)", "").Trim();
        int found = catalog.FindIndex(p => p && (p.id == baseName || (p.prefab && p.prefab.name == baseName)));
        if (found >= 0) SetIndex(found);
    }

    // ── rays / UI
    bool PointerOverUI() => EventSystem.current && EventSystem.current.IsPointerOverGameObject();

    bool RayToIsland(out RaycastHit hit)
    {
        if (!RayFromPointer(out hit)) return false;
        if (IsOnIsland(hit.collider)) return true;

        if (debugLogs)
            Debug.Log($"[Builder] Ray hit {hit.collider?.name ?? "<null>"} but not recognized as island.");
        return false;
    }

    bool IsOnIsland(Collider col)
    {
        if (!col) return false;

        // TEMP: accept ANY surface to verify ray + UI flow
        if (allowAnySurface) return true;

        // Direct match
        if (islandCollider && col == islandCollider) return true;

        // Under island root?
        if (islandRoot)
        {
            if (col.transform == islandRoot || col.transform.IsChildOf(islandRoot)) return true;
            var lp = col.GetComponentInParent<LowPolyIsland>();
            if (lp && lp.transform == islandRoot) return true;
        }

        // Name contains / tags (your decorator, etc.)
        if (acceptedSurfaceTags != null)
            for (int i = 0; i < acceptedSurfaceTags.Length; i++)
                if (!string.IsNullOrEmpty(acceptedSurfaceTags[i]) && col.CompareTag(acceptedSurfaceTags[i]))
                    return true;

        if (acceptDecoratorSurfaces && acceptedSurfaceNameContains != null)
            for (int i = 0; i < acceptedSurfaceNameContains.Length; i++)
                if (!string.IsNullOrEmpty(acceptedSurfaceNameContains[i]) &&
                    col.name.IndexOf(acceptedSurfaceNameContains[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

        return false;
    }


        bool RayFromPointer(out RaycastHit hit)
    {
        if (!cam) { hit = default; return false; }
        Vector2 mousePos = mouse != null ? mouse.position.ReadValue()
                                        : new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = cam.ScreenPointToRay(mousePos);
        int mask = surfaceMask.value == 0 ? ~0 : surfaceMask;
        var qti = includeTriggerColliders ? QueryTriggerInteraction.Collide
                                        : QueryTriggerInteraction.Ignore;
        bool ok = Physics.Raycast(ray, out hit, maxRayDistance, mask, qti);

        if (showHitGizmo && ok) Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.cyan, 0.02f);
        return ok;
    }


    // ── save/load (robust against shutdown)
    public void SafeSave()               { TrySave(false); }
    void SafeSaveSilently()              { TrySave(true); }
    void TrySave(bool silent)
    {
        try
        {
            if (!Application.isPlaying) return;
            if (buildRoot == null || !buildRoot) return;

            var data = new SaveData();
            int n = buildRoot.childCount; // may throw if buildRoot destroyed → protected by null check above
            for (int i = 0; i < n; i++)
            {
                var t = buildRoot.GetChild(i);
                if (!t) continue;

                string id = MatchPlaceableId(t.name);
                if (string.IsNullOrEmpty(id)) id = t.gameObject.name;

                data.items.Add(new Entry { id = id, pos = t.position, rot = t.rotation, scale = t.localScale });
            }
            System.IO.File.WriteAllText(SavePath, JsonUtility.ToJson(data, !silent));
#if UNITY_EDITOR
            if (!silent) Debug.Log($"[Builder] Saved {data.items.Count} → {SavePath}");
#endif
        }
        catch (MissingReferenceException) { /* ignore shutdown ordering */ }
        catch (Exception ex) { if (!silent) Debug.LogWarning($"[Builder] Save failed: {ex.Message}"); }
    }

    public void SafeLoad()
    {
        try
        {
            if (!System.IO.File.Exists(SavePath)) return;
            if (buildRoot == null || !buildRoot) return;

            for (int i = buildRoot.childCount - 1; i >= 0; i--)
            {
                var t = buildRoot.GetChild(i);
                if (t) Destroy(t.gameObject);
            }

            var data = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(SavePath));
            foreach (var e in data.items)
            {
                var def = catalog.Find(p => p && (p.id == e.id || (p.prefab && p.prefab.name == e.id)));
                if (def == null || !def.prefab) continue;
                var go = Instantiate(def.prefab, e.pos, e.rot, buildRoot);
                go.transform.localScale = e.scale;
            }
#if UNITY_EDITOR
            Debug.Log($"[Builder] Loaded {data.items.Count} from {SavePath}");
#endif
        }
        catch (Exception ex) { Debug.LogWarning($"[Builder] Load failed: {ex.Message}"); }
    }

    // ── utils
    void DisableScriptsByName()
    {
        autoDisabled.Clear();
        if (!playerRoot || extraScriptNamesToDisable == null) return;

        var all = playerRoot.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var m in all)
        {
            if (!m) continue;
            string tn = m.GetType().Name;
            for (int i = 0; i < extraScriptNamesToDisable.Length; i++)
            {
                if (string.Equals(tn, extraScriptNamesToDisable[i], StringComparison.Ordinal))
                {
                    if (m.enabled) { m.enabled = false; autoDisabled.Add(m); }
                    break;
                }
            }
        }
    }
    void ReenableScriptsByName()
    {
        foreach (var m in autoDisabled) if (m) m.enabled = true;
        autoDisabled.Clear();
    }

    Vector3 SnapXZ(Vector3 p, float size)
    {
        p.x = Mathf.Round(p.x / size) * size;
        p.z = Mathf.Round(p.z / size) * size;
        return p;
    }

    bool IsTooClose(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        for (int i = 0; i < buildRoot.childCount; i++)
        {
            var c = buildRoot.GetChild(i);
            if (!c) continue;
            Vector3 a = c.position; a.y = 0;
            Vector3 b = pos;        b.y = 0;
            if ((a - b).sqrMagnitude < r2) return true;
        }
        return false;
    }

    string MatchPlaceableId(string instanceName)
    {
        string clean = instanceName.Replace("(Clone)", "").Replace("(Placed)", "").Trim();
        foreach (var p in catalog)
            if (p && p.prefab && (p.id == clean || p.prefab.name == clean)) return p.id;
        return null;
    }
}
