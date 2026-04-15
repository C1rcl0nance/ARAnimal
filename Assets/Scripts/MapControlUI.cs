using TMPro;
using UnityEngine;

public class MapControlUI : MonoBehaviour
{
    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;

    [Header("UI (Optional)")]
    public TMP_InputField mapNameInput;
    public TMP_Text statusText;

    [Header("Default")]
    public string defaultMapName = "MyMap";
    public bool refreshStatusEveryFrame = true;

    void Start()
    {
        RefreshStatus();
    }

    void Update()
    {
        if (refreshStatusEveryFrame) RefreshStatus();
    }

    public void OnClickStartBuildMode()
    {
        if (worldAnchorManager == null)
        {
            Debug.LogError("[MapControlUI] worldAnchorManager 未绑定。");
            return;
        }
        worldAnchorManager.StartBuildMode();
        RefreshStatus();
    }

    public void OnClickHostCurrentMap()
    {
        if (worldAnchorManager == null)
        {
            Debug.LogError("[MapControlUI] worldAnchorManager 未绑定。");
            return;
        }
        worldAnchorManager.HostCurrentMap(GetMapNameOrDefault());
        RefreshStatus();
    }

    public void OnClickLoadSavedMap()
    {
        if (worldAnchorManager == null)
        {
            Debug.LogError("[MapControlUI] worldAnchorManager 未绑定。");
            return;
        }
        worldAnchorManager.LoadAndLocalizeSavedMap();
        RefreshStatus();
    }

    public void OnClickSaveState()
    {
        if (worldAnchorManager == null) return;
        worldAnchorManager.SaveState();
        RefreshStatus();
    }

    public void OnClickDeleteState()
    {
        if (worldAnchorManager == null) return;
        worldAnchorManager.DeleteSavedState();
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        if (statusText == null) return;

        if (worldAnchorManager == null)
        {
            statusText.text = "Map: 未绑定 WorldAnchorManager";
            return;
        }

        string mode = worldAnchorManager.IsBuilding ? "Building"
            : worldAnchorManager.IsLocalizing ? "Localizing"
            : worldAnchorManager.IsLocalized ? "Localized"
            : "Idle";

        string mapName = string.IsNullOrEmpty(worldAnchorManager.CurrentMapName) ? "(none)" : worldAnchorManager.CurrentMapName;
        string mapId = string.IsNullOrEmpty(worldAnchorManager.CurrentMapId) ? "(none)" : worldAnchorManager.CurrentMapId;
        string anchor = worldAnchorManager.AnchorRoot == null ? "null" : worldAnchorManager.AnchorRoot.name;

        statusText.text =
            $"Mode: {mode}\n" +
            $"MapName: {mapName}\n" +
            $"MapID: {mapId}\n" +
            $"Localized: {worldAnchorManager.IsLocalized}\n" +
            $"AnchorRoot: {anchor}";
    }

    private string GetMapNameOrDefault()
    {
        if (mapNameInput != null)
        {
            string value = mapNameInput.text == null ? "" : mapNameInput.text.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return string.IsNullOrWhiteSpace(defaultMapName) ? "MyMap" : defaultMapName.Trim();
    }
}
