using UnityEngine;

public class IMGUIProbe : MonoBehaviour
{
    public bool show = true;
    void OnGUI()
    {
        if (!show) return;
        // Force to top and cancel any global scaling/skins
        GUI.depth = -32000;
        GUI.matrix = Matrix4x4.identity;
        GUI.skin = null;
        GUI.color = Color.white; GUI.backgroundColor = Color.white; GUI.contentColor = Color.white;

        // Big pink bar so it cannot be missed
        GUI.Box(new Rect(20, 20, 360, 80), "IMGUI PROBE\nIf you can read this, OnGUI is working.");
    }
}
