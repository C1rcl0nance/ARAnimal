using TMPro;
using UnityEngine;

public class MapControlUI : MonoBehaviour
{
    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;
    public ExperienceModeCoordinator modeCoordinator;

    [Header("UI (Optional)")]
    public TMP_Text statusText;

    [Header("Runtime")]
    public bool refreshStatusEveryFrame = true;

    private void Start()
    {
        RefreshStatus();
    }

    private void Update()
    {
        if (refreshStatusEveryFrame)
        {
            RefreshStatus();
        }
    }

    public void OnClickStartBuildMode()
    {
        Debug.LogWarning("[MapControlUI] Deprecated: map building has been removed from app flow.");
    }

    public void OnClickHostCurrentMap()
    {
        Debug.LogWarning("[MapControlUI] Deprecated: map hosting has been removed from app flow.");
    }

    public void OnClickLoadSavedMap()
    {
        Debug.LogWarning("[MapControlUI] Deprecated: sparse map loading has been removed from app flow.");
    }

    public void OnClickSaveState()
    {
        if (worldAnchorManager == null)
        {
            return;
        }
        worldAnchorManager.SaveState();
        RefreshStatus();
    }

    public void OnClickDeleteState()
    {
        if (worldAnchorManager == null)
        {
            return;
        }
        worldAnchorManager.DeleteSavedState();
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        if (statusText == null)
        {
            return;
        }

        if (worldAnchorManager == null)
        {
            statusText.text = "Anchor: worldAnchorManager not assigned";
            return;
        }

        string mode = modeCoordinator == null ? "(no coordinator)" : modeCoordinator.CurrentMode.ToString();
        string localized = worldAnchorManager.IsLocalized ? "True" : "False";
        string placeId = string.IsNullOrEmpty(worldAnchorManager.CurrentPlaceId) ? "(none)" : worldAnchorManager.CurrentPlaceId;
        string anchor = worldAnchorManager.AnchorRoot == null ? "null" : worldAnchorManager.AnchorRoot.name;

        statusText.text =
            $"Mode: {mode}\n" +
            $"Localized: {localized}\n" +
            $"PlaceID: {placeId}\n" +
            $"AnchorRoot: {anchor}";
    }
}
