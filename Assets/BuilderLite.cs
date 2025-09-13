using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class BuilderLite : MonoBehaviour
{
    // ───────────────────────── CONFIG ─────────────────────────
    [Header("Scene")]
    public Camera mainCamera;                        // assign your gameplay cam
    public Transform buildRoot;                      // auto-created if empty
    public Transform playerRoot;                     // top-level player object (for freezing)

    [Header("Surfaces")]
    public LayerMask buildableLayers = ~0;           // use Everything to start
    public bool allowAnySurface = true;              // accept any collider (good for first run)
    public string[] acceptedSurfaceTags = { "BuildSurface" };

    [Header("Ghost")]
    public Material ghostMaterial;                   // optional translucent mat
    public Color ghostTint = new Color(0.7f, 1f, 0.7f, 0.5f);

    [Header("Placement")]
    public float maxRayDistance = 600f;
    public bool gridSnap = false;
    public float gridSize = 1f;

    [Header("Items & Tabs")]
    public List<BuildTab> buildTabs = new List<BuildTab>()
    {
        new BuildTab { tabName = "General", displayName = "General", tabColor = Color.white, description = "General building items" },
        new BuildTab { tabName = "Structures", displayName = "Buildings", tabColor = Color.cyan, description = "Houses, walls, and structures" },
        new BuildTab { tabName = "Decorations", displayName = "Decor", tabColor = Color.yellow, description = "Decorative items and furniture" },
        new BuildTab { tabName = "Utilities", displayName = "Tools", tabColor = Color.green, description = "Functional items and utilities" }
    };
    
    public List<Placeable> items = new List<Placeable>();

    [Header("IMGUI Menu (no Canvas)")]
    public bool openMenuOnEnter = true;              // menu opens when you press B
    public bool showMenu = false;                    // Tab toggles in play
    public bool centerMenuTemporarily = false;       // set true if you suspect off-screen
    public int gridColumns = 1;
    public float buttonHeight = 84f;
    public float buttonWidth  = 240f;
    public float menuPadding  = 12f;
    public bool closeMenuOnPick = true;

    [Header("IMGUI Debug")]
    public bool forceOnTop = true;                   // draw after everything else
    public bool drawCornerBadge = true;              // “IMGUI ACTIVE” at top-left
    public bool logRepaint = false;
    public bool alwaysShowMenuForDebug = true;

    [Header("Cursor Settings")]
    public bool confineCursorInBuild = false;
    public bool lockCursorOnExit = false;

    [Header("Build Mode Settings")]
    public bool allowPlayerMovement = true;  // Allow player to move while building
    public float placementDistance = 5f;     // How far in front of camera to place objects
    public bool autoSave = true;             // Auto-save when placing/deleting objects
    public string saveFileName = "island_build_default.json";
    
    [Header("Networking")]
    public NetworkedBuilder networkedBuilder; // Reference to networking component
    
    [Header("Preview Settings")]
    public bool generatePreviews = true;     // Generate 3D preview images of prefabs
    public int previewSize = 64;             // Preview texture size (64x64)
    
    [Header("Player Freeze (when allowPlayerMovement = false)")]
    public string[] autoDisableScriptNames = { "PlayerController" };
    public bool freezePlayerRigidbodies = true;
    public bool disableCharacterControllers = true;

    // ───────────────────────── RUNTIME ─────────────────────────
    [Serializable]
    public class Placeable
    {
        public string id = "Item";
        public string displayName = "Item";
        public Sprite icon;                          // optional
        public GameObject prefab;                    // REQUIRED
        public bool alignToSurfaceNormal = false;
        public float yOffset = 0.03f;
        public float rotateStepDegrees = 15f;
        public float footprintRadius = 0.6f;         // spacing check
        [Space]
        public string category = "General";          // Which tab this item belongs to
    }

    [System.Serializable]
    public class BuildTab
    {
        public string tabName = "General";
        public string displayName = "General";
        public Color tabColor = Color.white;
        [TextArea(2,4)]
        public string description = "General building items";
    }

    // ───────────────────────── SAVE DATA ─────────────────────────
    [System.Serializable]
    public class PlacedObjectData
    {
        public string itemId;
        public float posX, posY, posZ;
        public float rotX, rotY, rotZ, rotW;  // quaternion
        
        public PlacedObjectData() { }
        
        public PlacedObjectData(string id, Vector3 position, Quaternion rotation)
        {
            itemId = id;
            posX = position.x; posY = position.y; posZ = position.z;
            rotX = rotation.x; rotY = rotation.y; rotZ = rotation.z; rotW = rotation.w;
        }
        
        public Vector3 Position => new Vector3(posX, posY, posZ);
        public Quaternion Rotation => new Quaternion(rotX, rotY, rotZ, rotW);
    }

    [System.Serializable]
    public class IslandSaveData
    {
        public List<PlacedObjectData> placedObjects = new List<PlacedObjectData>();
    }

    bool buildMode;
    int selected = -1;
    int currentTab = 0;  // Currently selected tab index
    GameObject ghost; Collider[] ghostCols;
    float yaw; Vector2 scroll; Rect lastMenuRect;
    bool mouseOverMenu; bool suppressClickUntilRelease;

    // click vs drag
    bool lmbDown; Vector2 lmbDownPos; bool isDragging;
    const float DRAG_THRESHOLD_PIXELS = 6f;

    // cursor cache
    CursorLockMode prevLock; bool prevVisible;
    

    // freeze caches
    readonly List<MonoBehaviour> disabledScripts = new();
    struct RbState { public Rigidbody rb; public bool kin; public bool grav; public RigidbodyConstraints cons; public Vector3 vel; }
    readonly List<RbState> rbStates = new();
    struct CcState { public CharacterController cc; public bool en; }
    readonly List<CcState> ccStates = new();

    // IMGUI helpers
    static Texture2D _solidTex;
    static float _lastRepaintLog;
    
    // Preview system
    Dictionary<string, Texture2D> previewCache = new Dictionary<string, Texture2D>();
    Camera previewCamera;

    void Awake()
    {
        if (!mainCamera) mainCamera = Camera.main;
        if (!buildRoot)
        {
            var go = GameObject.Find("_BUILD_ROOT");
            if (!go) go = new GameObject("_BUILD_ROOT");
            buildRoot = go.transform;
        }
    }

    void Start()
    {
        // Auto-load saved island on game start
        LoadIsland();
        
        // Generate preview images for all items
        if (generatePreviews) GenerateAllPreviews();

        // Auto-wire NetworkedBuilder if present
        if (networkedBuilder == null)
        {
            networkedBuilder = FindObjectOfType<NetworkedBuilder>();
        }
    }


    void Update()
    {
        // Toggle build mode
        if (Input.GetKeyDown(KeyCode.B))
        {
            buildMode = !buildMode;
            if (buildMode) EnterBuildMode(); else ExitBuildMode();
        }

        if (!buildMode)
        {
            if (ghost) ghost.SetActive(false);
            return;
        }

        // Menu toggle with cursor control
        if (Input.GetKeyDown(KeyCode.Tab)) 
        {
            showMenu = !showMenu;
            
            if (showMenu)
            {
                // Menu opened - unlock cursor and make it visible
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                // Menu closed - restore FPS cursor state (locked and hidden)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // Rotate & grid
        if (Input.GetKeyDown(KeyCode.Q)) yaw -= Current?.rotateStepDegrees ?? 15f;
        if (Input.GetKeyDown(KeyCode.E)) yaw += Current?.rotateStepDegrees ?? 15f;
        if (Input.GetKeyDown(KeyCode.G)) gridSnap = !gridSnap;

        // Rotate with mouse scroll wheel
        if (buildMode && !showMenu && Current != null)
        {
            float scrollDelta = Input.mouseScrollDelta.y;
            if (scrollDelta != 0)
            {
                float step = Current.rotateStepDegrees;
                if (scrollDelta > 0)
                    yaw += step;
                else
                    yaw -= step;
            }
        }

        // Disable drag when menu is visible
        if (showMenu) { lmbDown = false; isDragging = false; }

        // Ghost follows mouse
        UpdateGhost();

        // Click / Drag
        if (suppressClickUntilRelease)
        {
            if (Input.GetMouseButtonUp(0)) suppressClickUntilRelease = false;
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
            {
                lmbDown = true; isDragging = false; lmbDownPos = Input.mousePosition;
            }
            else if (lmbDown && Input.GetMouseButton(0))
            {
                if (!isDragging)
                {
                    if (((Vector2)Input.mousePosition - lmbDownPos).magnitude > DRAG_THRESHOLD_PIXELS &&
                        !mouseOverMenu && !showMenu)
                    {
                        isDragging = true;
                    }
                }
                // No drag pan in first person mode
            }
            else if (lmbDown && Input.GetMouseButtonUp(0))
            {
                if (!isDragging && !mouseOverMenu) TryPlace();
                lmbDown = false; isDragging = false;
            }

            if (Input.GetMouseButtonDown(1) && !mouseOverMenu) TryDelete();

            if (!mouseOverMenu && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetMouseButtonDown(0))
            {
                TryPick();
                lmbDown = false; isDragging = false;
            }
        }
    }

    void OnGUI()
    {
        // Only show GUI in build mode
        if (!buildMode) 
        {
            mouseOverMenu = false;
            return;
        }
        
        // Draw crosshair in center of screen
        float crosshairSize = 20f;
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;
        GUI.color = new Color(1, 1, 1, 0.5f);
        GUI.Box(new Rect(centerX - 1, centerY - crosshairSize/2, 2, crosshairSize), "");
        GUI.Box(new Rect(centerX - crosshairSize/2, centerY - 1, crosshairSize, 2), "");
        
        // Build mode indicator at top
        GUI.color = new Color(1, 1, 1, 0.8f);
        GUI.Box(new Rect(Screen.width/2 - 100, 10, 200, 30), "BUILD MODE - Press B to Exit");
        
        // Show current item at top
        if (selected >= 0 && selected < items.Count && items[selected] != null)
        {
            string currentItem = items[selected].displayName;
            if (string.IsNullOrEmpty(currentItem)) currentItem = items[selected].id;
            GUI.Box(new Rect(Screen.width/2 - 100, 45, 200, 25), "Selected: " + currentItem);
        }
        
        // Only show menu if Tab is pressed
        if (!showMenu)
        {
            GUI.Label(new Rect(Screen.width/2 - 100, Screen.height - 40, 200, 30), "Press TAB for items");
            mouseOverMenu = false;
            return;
        }
        
        // Draw horizontal menu bar at bottom (increased height for tabs)
        float menuHeight = 140f;
        float menuY = Screen.height - menuHeight;
        Rect menuRect = new Rect(0, menuY, Screen.width, menuHeight);
        
        // Menu background
        GUI.color = new Color(0, 0, 0, 0.9f);
        GUI.Box(menuRect, "");
        GUI.color = Color.white;
        
        // Draw tabs at the top
        float tabHeight = 25f;
        float tabY = menuY + 5f;
        float totalTabWidth = 0f;
        
        // Calculate total tab width
        for (int i = 0; i < buildTabs.Count; i++)
        {
            totalTabWidth += Mathf.Max(100f, buildTabs[i].displayName.Length * 8f + 20f);
        }
        
        float tabStartX = (Screen.width - totalTabWidth) / 2f;
        float currentTabX = tabStartX;
        
        // Draw each tab
        for (int i = 0; i < buildTabs.Count; i++)
        {
            var tab = buildTabs[i];
            float tabWidth = Mathf.Max(100f, tab.displayName.Length * 8f + 20f);
            
            // Tab button background
            Color tabBgColor = (i == currentTab) ? new Color(tab.tabColor.r, tab.tabColor.g, tab.tabColor.b, 0.8f) 
                                                 : new Color(0.3f, 0.3f, 0.3f, 0.7f);
            GUI.color = tabBgColor;
            GUI.Box(new Rect(currentTabX, tabY, tabWidth, tabHeight), "");
            
            // Tab button click detection
            GUI.color = Color.white;
            if (GUI.Button(new Rect(currentTabX, tabY, tabWidth, tabHeight), "", GUIStyle.none))
            {
                if (currentTab != i)
                {
                    currentTab = i;
                    selected = -1; // Reset selection when changing tabs
                }
            }
            
            // Tab text
            var tabStyle = new GUIStyle(GUI.skin.label);
            tabStyle.alignment = TextAnchor.MiddleCenter;
            tabStyle.fontSize = 11;
            tabStyle.fontStyle = (i == currentTab) ? FontStyle.Bold : FontStyle.Normal;
            GUI.color = Color.white;
            GUI.Label(new Rect(currentTabX, tabY, tabWidth, tabHeight), tab.displayName, tabStyle);
            
            currentTabX += tabWidth + 2f;
        }
        
        // Get items for current tab
        var currentTabItems = GetItemsForTab(buildTabs.Count > currentTab ? buildTabs[currentTab].tabName : "General");
        
        // Calculate button layout for current tab items
        int itemCount = currentTabItems.Count;
        
        float buttonWidth = Mathf.Min(120f, (Screen.width - 20f) / Mathf.Max(1, itemCount));
        float buttonHeight = 90f; // Increased height for image + text
        float startX = (Screen.width - (buttonWidth * itemCount)) / 2f;
        
        // Draw item buttons for current tab
        int btnIndex = 0;
        for (int i = 0; i < currentTabItems.Count; i++)
        {
            var item = currentTabItems[i];
            if (item == null || item.prefab == null) continue;
            
            float btnX = startX + (btnIndex * buttonWidth);
            float btnY = menuY + 35f; // Move down to make room for tabs
            
            // Find the actual index of this item in the main items list
            int actualIndex = items.IndexOf(item);
            
            // Highlight selected item
            if (actualIndex == selected)
            {
                GUI.color = new Color(0.2f, 0.5f, 0.2f, 1f);
                GUI.Box(new Rect(btnX - 2, btnY - 2, buttonWidth, buttonHeight + 4), "");
                GUI.color = Color.white;
            }
            
            // Draw button background
            bool clicked = GUI.Button(new Rect(btnX, btnY, buttonWidth - 5, buttonHeight), "", GUI.skin.button);
            
            // Draw preview image
            var preview = GetPreviewForItem(item);
            if (preview != null)
            {
                float imageSize = 48f;
                float imageX = btnX + (buttonWidth - 5 - imageSize) / 2f;
                float imageY = btnY + 5f;
                GUI.DrawTexture(new Rect(imageX, imageY, imageSize, imageSize), preview);
            }
            
            // Draw text below image
            string buttonText = item.displayName;
            if (string.IsNullOrEmpty(buttonText)) buttonText = item.id;
            
            GUI.color = Color.white;
            var textStyle = new GUIStyle(GUI.skin.label);
            textStyle.alignment = TextAnchor.MiddleCenter;
            textStyle.fontSize = 10;
            textStyle.wordWrap = true;
            
            GUI.Label(new Rect(btnX, btnY + 55f, buttonWidth - 5, 30f), buttonText, textStyle);
            
            if (clicked)
            {
                Select(actualIndex); // Use the actual index from the main items list
                showMenu = false; // Close menu after selection
                
                // Restore FPS cursor state when menu closes
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            
            btnIndex++;
        }
        
        // Track mouse over menu
        Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        mouseOverMenu = menuRect.Contains(mousePos);
    }

    // ───────────────────── build mode enter/exit ─────────────────────
    void EnterBuildMode()
    {
        showMenu = false; // Start with menu closed

        // Only freeze player if not allowing movement
        if (!allowPlayerMovement) FreezePlayer();

        // Save cursor state but don't change it yet - only change when menu opens
        prevLock = Cursor.lockState; 
        prevVisible = Cursor.visible;

        // Select first item if none selected
        if (selected < 0 && items.Count > 0) Select(0);
    }

    void ExitBuildMode()
    {
        if (!allowPlayerMovement) UnfreezePlayer();

        // Always restore original cursor state when exiting build mode
        Cursor.lockState = prevLock;
        Cursor.visible = prevVisible;

        if (ghost) ghost.SetActive(false);
        showMenu = false;
        lmbDown = false; 
        isDragging = false;
    }

    // ───────────────────── player freeze / unfreeze ─────────────────────
    void FreezePlayer()
    {
        disabledScripts.Clear(); rbStates.Clear(); ccStates.Clear();
        if (!playerRoot) return;

        // disable movement/look scripts by simple name
        var all = playerRoot.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var m in all)
        {
            if (!m) continue;
            string tn = m.GetType().Name;
            for (int i = 0; i < autoDisableScriptNames.Length; i++)
                if (tn == autoDisableScriptNames[i])
                {
                    if (m.enabled) { m.enabled = false; disabledScripts.Add(m); }
                    break;
                }
        }

        if (freezePlayerRigidbodies)
        {
            foreach (var rb in playerRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!rb) continue;
                rbStates.Add(new RbState { rb = rb, kin = rb.isKinematic, grav = rb.useGravity, cons = rb.constraints, vel = rb.linearVelocity });
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

        if (disableCharacterControllers)
        {
            foreach (var cc in playerRoot.GetComponentsInChildren<CharacterController>(true))
            {
                if (!cc) continue;
                ccStates.Add(new CcState { cc = cc, en = cc.enabled });
                cc.enabled = false;
            }
        }
    }

    void UnfreezePlayer()
    {
        foreach (var m in disabledScripts) if (m) m.enabled = true;
        disabledScripts.Clear();

        for (int i = 0; i < rbStates.Count; i++)
        {
            var s = rbStates[i];
            if (!s.rb) continue;
            s.rb.constraints = s.cons;
            s.rb.isKinematic = s.kin;
            s.rb.useGravity = s.grav;
            s.rb.linearVelocity = s.vel;
        }
        rbStates.Clear();

        for (int i = 0; i < ccStates.Count; i++)
        {
            var s = ccStates[i];
            if (s.cc) s.cc.enabled = s.en;
        }
        ccStates.Clear();
    }


    // ───────────────────── ghost & placement ─────────────────────
    Placeable Current => (selected >= 0 && selected < items.Count) ? items[selected] : null;

    public void Select(int i)
    {
        selected = Mathf.Clamp(i, 0, Mathf.Max(0, items.Count - 1));
        yaw = 0f;
        SpawnGhost();
    }

    void SpawnGhost()
    {
        if (ghost) Destroy(ghost);
        var def = Current; if (def == null || !def.prefab) return;

        ghost = Instantiate(def.prefab);
        ghost.name = "[GHOST] " + (string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName);

        if (ghostMaterial)
        {
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) mats[m] = ghostMaterial;
                r.sharedMaterials = mats;
            }
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ghostTint);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", ghostTint);
            }
        }

        ghostCols = ghost.GetComponentsInChildren<Collider>(true);
        foreach (var c in ghostCols) if (c) c.enabled = false;
    }

    void UpdateGhost()
    {
        var def = Current;
        if (def == null || !def.prefab) { if (ghost) ghost.SetActive(false); return; }
        if (!mainCamera) { if (ghost) ghost.SetActive(false); return; }

        // Cast ray from center of screen instead of mouse position
        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));

        // First try to hit a surface
        if (Physics.Raycast(ray, out var hit, maxRayDistance, buildableLayers, QueryTriggerInteraction.Collide))
        {
            if (IsBuildSurface(hit.collider))
            {
                Vector3 pos = hit.point;
                if (gridSnap) pos = SnapXZ(pos, gridSize);
                pos += hit.normal * def.yOffset;

                // Yaw is always relative to camera's Y rotation
                float baseYaw = mainCamera.transform.eulerAngles.y;
                Quaternion rot = Quaternion.Euler(0f, baseYaw + yaw, 0f);
                if (def.alignToSurfaceNormal)
                    rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * rot;

                if (!ghost) SpawnGhost();
                if (ghost)
                {
                    ghost.transform.SetPositionAndRotation(pos, rot);
                    ghost.SetActive(true);
                }
                return;
            }
        }

        // If no valid surface hit, place at fixed distance
        Vector3 defaultPos = mainCamera.transform.position + mainCamera.transform.forward * placementDistance;
        float camYaw = mainCamera.transform.eulerAngles.y;
        Quaternion defaultRot = Quaternion.Euler(0f, camYaw + yaw, 0f);

        if (!ghost) SpawnGhost();
        if (ghost)
        {
            ghost.transform.SetPositionAndRotation(defaultPos, defaultRot);
            ghost.SetActive(true);
        }
    }

    bool IsBuildSurface(Collider col)
    {
        if (allowAnySurface) return true;
        if (!col) return false;
        if (acceptedSurfaceTags != null)
            for (int i = 0; i < acceptedSurfaceTags.Length; i++)
                if (!string.IsNullOrEmpty(acceptedSurfaceTags[i]) && col.CompareTag(acceptedSurfaceTags[i]))
                    return true;
        return true; // layer mask already filtered
    }

    void TryPlace()
    {
        var def = Current;
        if (!buildMode || def == null || def.prefab == null || !ghost || !ghost.activeSelf) return;
        
        // Don't place if menu is open
        if (showMenu) return;
        
        if (IsTooClose(ghost.transform.position, def.footprintRadius)) return;

        var go = Instantiate(def.prefab, ghost.transform.position, ghost.transform.rotation, buildRoot);
        go.name = def.id + " (Placed)";
        
        // Notify network
        if (networkedBuilder != null)
        {
            networkedBuilder.NotifyObjectPlaced(def.id, ghost.transform.position, ghost.transform.rotation);
        }
        
        // Auto-save after placing
        if (autoSave) SaveIsland();
    }

    void TryDelete()
    {
        if (!mainCamera) return;
        // Cast ray from center of screen
        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
        if (!Physics.Raycast(ray, out var hit, maxRayDistance, ~0, QueryTriggerInteraction.Collide)) return;

        Transform t = hit.transform;
        while (t && t.parent != buildRoot) t = t.parent;
        if (t && t.parent == buildRoot) 
        {
            Vector3 deletePosition = t.position;
            Destroy(t.gameObject);
            
            // Notify network
            if (networkedBuilder != null)
            {
                networkedBuilder.NotifyObjectDeleted(deletePosition);
            }
            
            // Auto-save after deleting
            if (autoSave) SaveIsland();
        }
    }

    void TryPick()
    {
        if (!mainCamera) return;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, maxRayDistance, ~0, QueryTriggerInteraction.Collide)) return;

        Transform root = hit.transform;
        while (root && root.parent != buildRoot) root = root.parent;
        if (!root) return;

        string baseName = root.name.Replace("(Placed)", "").Replace("(Clone)", "").Trim();
        int found = items.FindIndex(p => p != null && (p.id == baseName || (p.prefab && p.prefab.name == baseName)));
        if (found >= 0) Select(found);
    }

    // ───────────────────── IMGUI helpers ─────────────────────
    struct GuiState
    {
        public int depth;
        public Color color, bg, ct;
        public GUISkin skin;
        public Matrix4x4 mx;
    }
    GuiState _gs;

    void PushGUI()
    {
        _gs.depth = GUI.depth; _gs.color = GUI.color; _gs.bg = GUI.backgroundColor; _gs.ct = GUI.contentColor; _gs.skin = GUI.skin; _gs.mx = GUI.matrix;
        GUI.depth = forceOnTop ? -30000 : _gs.depth;
        GUI.matrix = Matrix4x4.identity;
        GUI.skin = null;
        GUI.enabled = true;
        GUI.color = Color.white; GUI.backgroundColor = Color.white; GUI.contentColor = Color.white;
    }
    void PopGUI()
    {
        GUI.matrix = _gs.mx; GUI.skin = _gs.skin; GUI.color = _gs.color; GUI.backgroundColor = _gs.bg; GUI.contentColor = _gs.ct; GUI.depth = _gs.depth;
    }

    static void EnsureSolidTex()
    {
        if (_solidTex) return;
        _solidTex = new Texture2D(1,1,TextureFormat.RGBA32, false);
        _solidTex.SetPixel(0,0, Color.white);
        _solidTex.Apply();
    }
    static void DrawSolidRect(Rect r, Color c)
    {
        EnsureSolidTex();
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, _solidTex);
        GUI.color = old;
    }
    static void DrawOutlineRect(Rect r, Color c, float t = 1f)
    {
        EnsureSolidTex();
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), _solidTex);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), _solidTex);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), _solidTex);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), _solidTex);
        GUI.color = old;
    }

    // ───────────────────── styles & utils ─────────────────────
    static GUIStyle EditorBold()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontStyle = FontStyle.Bold; s.fontSize = 14;
        return s;
    }
    static GUIStyle Hint()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize = 11; s.normal.textColor = Color.gray;
        return s;
    }
    static void DrawSprite(Rect r, Sprite s)
    {
        if (!s) return;
        Texture2D tex = s.texture;
        Rect tr = s.textureRect;
        Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
        GUI.DrawTextureWithTexCoords(r, tex, uv, true);
    }
    static Vector3 SnapXZ(Vector3 p, float size)
    {
        p.x = Mathf.Round(p.x / size) * size;
        p.z = Mathf.Round(p.z / size) * size;
        return p;
    }
    bool IsTooClose(Vector3 pos, float radius)
    {
        if (!buildRoot) return false;
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

    // ───────────────────── SAVE / LOAD SYSTEM ─────────────────────
    string SaveFilePath => Path.Combine(Application.persistentDataPath, saveFileName);

    void SaveIsland()
    {
        try
        {
            var saveData = new IslandSaveData();
            
            // Collect all placed objects
            if (buildRoot)
            {
                for (int i = 0; i < buildRoot.childCount; i++)
                {
                    var child = buildRoot.GetChild(i);
                    if (!child || !child.gameObject) continue;
                    
                    // Extract item ID from the object name
                    string itemId = child.name.Replace(" (Placed)", "").Replace("(Clone)", "").Trim();
                    
                    var objectData = new PlacedObjectData(itemId, child.position, child.rotation);
                    saveData.placedObjects.Add(objectData);
                }
            }
            
            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SaveFilePath, json);
            
            Debug.Log($"[BuilderLite] Saved {saveData.placedObjects.Count} objects to {SaveFilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BuilderLite] Failed to save island: {e.Message}");
        }
    }

    void LoadIsland()
    {
        try
        {
            if (!File.Exists(SaveFilePath))
            {
                Debug.Log($"[BuilderLite] No save file found at {SaveFilePath}");
                return;
            }
            
            string json = File.ReadAllText(SaveFilePath);
            var saveData = JsonUtility.FromJson<IslandSaveData>(json);
            
            if (saveData == null || saveData.placedObjects == null)
            {
                Debug.LogWarning("[BuilderLite] Invalid save data");
                return;
            }
            
            // Clear existing objects
            ClearAllObjects();
            
            // Spawn saved objects
            int loadedCount = 0;
            foreach (var objData in saveData.placedObjects)
            {
                var prefab = FindPrefabById(objData.itemId);
                if (prefab)
                {
                    var go = Instantiate(prefab, objData.Position, objData.Rotation, buildRoot);
                    go.name = objData.itemId + " (Placed)";
                    loadedCount++;
                }
                else
                {
                    Debug.LogWarning($"[BuilderLite] Could not find prefab for item ID: {objData.itemId}");
                }
            }
            
            Debug.Log($"[BuilderLite] Loaded {loadedCount} objects from {SaveFilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BuilderLite] Failed to load island: {e.Message}");
        }
    }

    public GameObject FindPrefabById(string itemId)
    {
        foreach (var item in items)
        {
            if (item != null && item.prefab != null && 
                (item.id == itemId || item.prefab.name == itemId))
            {
                return item.prefab;
            }
        }
        return null;
    }

    void ClearAllObjects()
    {
        if (!buildRoot) return;
        
        // Destroy all children
        for (int i = buildRoot.childCount - 1; i >= 0; i--)
        {
            var child = buildRoot.GetChild(i);
            if (child) DestroyImmediate(child.gameObject);
        }
    }

    // ───────────────────── PREVIEW GENERATION ─────────────────────
    void GenerateAllPreviews()
    {
        if (!generatePreviews) return;
        
        SetupPreviewCamera();
        
        foreach (var item in items)
        {
            if (item != null && item.prefab != null)
            {
                GeneratePreviewForItem(item);
            }
        }
        
        CleanupPreviewCamera();
    }

    void SetupPreviewCamera()
    {
        if (previewCamera != null) return;
        
        // Create a temporary camera for preview generation
        var camGO = new GameObject("_PreviewCamera");
        camGO.transform.position = new Vector3(1000, 1000, 1000); // Far away from scene
        previewCamera = camGO.AddComponent<Camera>();
        
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0f);
        previewCamera.cullingMask = ~0; // Render everything
        previewCamera.orthographic = false;
        previewCamera.fieldOfView = 30f;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 100f;
        previewCamera.enabled = false; // Don't render automatically
    }

    void GeneratePreviewForItem(Placeable item)
    {
        if (previewCache.ContainsKey(item.id)) return;
        
        try
        {
            // Instantiate prefab at preview location
            var previewObj = Instantiate(item.prefab);
            previewObj.transform.position = previewCamera.transform.position + Vector3.forward * 5f;
            
            // Calculate bounds to frame the object properly
            var bounds = CalculateBounds(previewObj);
            var distance = Mathf.Max(bounds.size.magnitude * 1.5f, 2f);
            
            // Position camera to frame the object
            previewCamera.transform.position = previewObj.transform.position + Vector3.back * distance + Vector3.up * (distance * 0.3f);
            previewCamera.transform.LookAt(previewObj.transform.position + bounds.center);
            
            // Create render texture
            var renderTexture = RenderTexture.GetTemporary(previewSize, previewSize, 16);
            previewCamera.targetTexture = renderTexture;
            
            // Render the preview
            previewCamera.Render();
            
            // Convert to Texture2D
            RenderTexture.active = renderTexture;
            var preview = new Texture2D(previewSize, previewSize, TextureFormat.RGBA32, false);
            preview.ReadPixels(new Rect(0, 0, previewSize, previewSize), 0, 0);
            preview.Apply();
            
            // Cache the preview
            previewCache[item.id] = preview;
            
            // Cleanup
            RenderTexture.active = null;
            previewCamera.targetTexture = null;
            RenderTexture.ReleaseTemporary(renderTexture);
            DestroyImmediate(previewObj);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BuilderLite] Failed to generate preview for {item.id}: {e.Message}");
        }
    }

    Bounds CalculateBounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.one);
        
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    void CleanupPreviewCamera()
    {
        if (previewCamera != null)
        {
            DestroyImmediate(previewCamera.gameObject);
            previewCamera = null;
        }
    }

    Texture2D GetPreviewForItem(Placeable item)
    {
        if (item == null) return null;
        previewCache.TryGetValue(item.id, out var preview);
        return preview;
    }

    // ───────────────────── TAB SYSTEM ─────────────────────
    List<Placeable> GetItemsForTab(string tabName)
    {
        var result = new List<Placeable>();
        foreach (var item in items)
        {
            if (item != null && item.prefab != null && item.category == tabName)
            {
                result.Add(item);
            }
        }
        
        // If no items found for this tab, return items with no category set (fallback to General)
        if (result.Count == 0 && tabName == "General")
        {
            foreach (var item in items)
            {
                if (item != null && item.prefab != null && 
                    (string.IsNullOrEmpty(item.category) || item.category == "General"))
                {
                    result.Add(item);
                }
            }
        }
        
        return result;
    }
    
}
