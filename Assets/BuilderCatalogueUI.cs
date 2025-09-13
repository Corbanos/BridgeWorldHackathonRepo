using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class BuilderCatalogUI : MonoBehaviour
{
    [Header("Wiring")]
    public BuilderTool builder;
    public GameObject panel;            // Whole panel (active/inactive). If null, uses this GameObject.
    public Transform gridParent;        // Parent with Grid/Vertical/Horizontal LayoutGroup
    public Button buttonPrefab;         // OPTIONAL template; leave null to auto-create buttons
    public bool closeOnPick = true;

    [Header("Header (optional)")]
    public string headerLabel = "Items";
    public TMP_Text headerTMP;
    public Text headerText;

    [Header("Runtime Button (if no prefab)")]
    public Vector2 minButtonSize = new(110, 110);
    public Vector2 paddingInside = new(10, 10); // icon/label padding

    static BuilderCatalogUI s_instance;
    int _lastCount = -1;

    void Awake()
    {
        s_instance = this;
        if (!builder) builder = FindObjectOfType<BuilderTool>();
        if (!panel) panel = gameObject;

        // Ensure EventSystem + GraphicRaycaster exist so clicks work
        if (!FindObjectOfType<EventSystem>())
        {
            var ev = new GameObject("EventSystem", typeof(EventSystem));
            ev.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        var canvas = GetComponentInParent<Canvas>();
        if (canvas && !canvas.TryGetComponent<GraphicRaycaster>(out _))
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        panel.SetActive(false);
    }

    void OnEnable()
    {
        if (panel.activeSelf) RebuildButtons();
    }

    // Called by BuilderTool when user presses Tab
    public static void ToggleGlobalPanel()
    {
        if (!s_instance) return;
        s_instance.Toggle();
    }

    public void Toggle()
    {
        if (!panel) return;
        bool show = !panel.activeSelf;
        if (show) RebuildButtons();
        panel.SetActive(show);
        UpdateHeader();
    }

    public void Show(bool show)
    {
        if (!panel) return;
        if (show) RebuildButtons();
        panel.SetActive(show);
        UpdateHeader();
    }

    void UpdateHeader()
    {
        if (headerTMP) headerTMP.text = headerLabel;
        if (headerText) headerText.text = headerLabel;
    }

    void RebuildButtons()
    {
        if (!gridParent || !builder)
        {
            Debug.LogWarning("[CatalogUI] Missing gridParent or builder reference.");
            return;
        }

        // Ensure a LayoutGroup is present so children get arranged.
        if (!gridParent.GetComponent<LayoutGroup>())
        {
            Debug.LogWarning("[CatalogUI] gridParent has no LayoutGroup. Add GridLayoutGroup/Vertical/Horizontal.");
        }

        // Clear previous buttons
        for (int i = gridParent.childCount - 1; i >= 0; i--)
            Destroy(gridParent.GetChild(i).gameObject);

        // Build one button per Placeable
        for (int i = 0; i < builder.catalog.Count; i++)
        {
            int idx = i;
            var def = builder.catalog[i];
            if (!def) continue;

            Button btn = buttonPrefab ? Instantiate(buttonPrefab, gridParent) : CreateRuntimeButton(gridParent);

            // Try to find an Image for icon anywhere under the button (excluding backgrounds if you want)
            var img = FindFirstIconImage(btn.transform);
            if (img && def.icon) { img.sprite = def.icon; img.preserveAspect = true; }

            // Set label (TMP first, fallback to legacy)
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp) { tmp.text = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName; }
            else
            {
                var legacy = btn.GetComponentInChildren<Text>(true);
                if (legacy) legacy.text = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName;
            }

            // Wire click
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (builder) builder.SelectFromUI(idx); // sets ghost + suppresses click
                if (closeOnPick && panel) panel.SetActive(false);
            });
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(gridParent as RectTransform);
        _lastCount = builder.catalog.Count;
    }

    // Create a simple button if you didn't provide a prefab
    Button CreateRuntimeButton(Transform parent)
    {
        var root = new GameObject("ItemButton",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        var rt = (RectTransform)root.transform;

        // Background image (raycast target ON)
        var bg = root.GetComponent<Image>();
        bg.raycastTarget = true;

        var le = root.GetComponent<LayoutElement>();
        le.preferredWidth  = Mathf.Max(minButtonSize.x, 0);
        le.preferredHeight = Mathf.Max(minButtonSize.y, 0);

        // Icon child
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(root.transform, false);
        var iconRT = (RectTransform)iconGO.transform;
        iconRT.anchorMin = new Vector2(0, 0);
        iconRT.anchorMax = new Vector2(1, 1);
        iconRT.offsetMin = new Vector2(paddingInside.x, paddingInside.y + 22f);
        iconRT.offsetMax = new Vector2(-paddingInside.x, -paddingInside.y);

        var iconImg = iconGO.GetComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        // Label child (bottom strip)
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(root.transform, false);
        var lrt = (RectTransform)labelGO.transform;
        lrt.anchorMin = new Vector2(0, 0);
        lrt.anchorMax = new Vector2(1, 0);
        lrt.pivot = new Vector2(0.5f, 0);
        lrt.sizeDelta = new Vector2(0, 22f);
        lrt.anchoredPosition = new Vector2(0, paddingInside.y);

        if (HasTMP())
        {
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "Item";
            tmp.alignment = TextAlignmentOptions.Midline;
            tmp.fontSize = 18;
            tmp.enableAutoSizing = true;
            tmp.raycastTarget = false;
        }
        else
        {
            var legacy = labelGO.AddComponent<Text>();
            legacy.text = "Item";
            legacy.alignment = TextAnchor.MiddleCenter;
            legacy.resizeTextForBestFit = true;
            legacy.raycastTarget = false;
        }

        return root.GetComponent<Button>();
    }

    Image FindFirstIconImage(Transform t)
    {
        // Prefer an Image that's NOT the root background if possible
        foreach (var img in t.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject == t.gameObject) continue; // skip root bg
            return img;
        }
        // fallback to root image
        return t.GetComponent<Image>();
    }

    bool HasTMP() => typeof(TextMeshProUGUI) != null;
}
