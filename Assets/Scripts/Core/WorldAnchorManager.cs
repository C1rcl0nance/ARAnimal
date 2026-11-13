using System.IO;
using UnityEngine;

public class WorldAnchorManager : MonoBehaviour
{
    [Header("Anchor")]
    public Transform anchorRoot;
    public string anchorRootName = "AnchorRoot";
    public bool autoLoadOnStart = true;
    public bool autoSaveOnChange = true;

    [Header("Runtime (Read Only)")]
    [SerializeField] private bool isLocalized;
    [SerializeField] private string currentPlaceId = "";

    public bool IsLocalized => isLocalized;
    public string CurrentPlaceId => currentPlaceId;
    public Transform AnchorRoot => anchorRoot;
    public string SaveFilePath => Path.Combine(Application.persistentDataPath, "WorldAnchorState.json");

    [System.Serializable]
    private class WorldAnchorState
    {
        public bool isLocalized;
        public string currentPlaceId;
        public bool hasAnchorRoot;
        public float[] anchorPosition;
        public float[] anchorRotation;
    }

    private void Start()
    {
        if (autoLoadOnStart)
        {
            LoadState();
        }
    }

    public void SetLocalized(bool value)
    {
        isLocalized = value;
        if (!value)
        {
            currentPlaceId = "";
        }
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void SetLocalized(bool value, string placeId)
    {
        isLocalized = value;
        currentPlaceId = value && !string.IsNullOrWhiteSpace(placeId) ? placeId.Trim() : "";
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void SetCurrentPlaceId(string placeId)
    {
        currentPlaceId = string.IsNullOrWhiteSpace(placeId) ? "" : placeId.Trim();
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void EnsureAnchorRoot()
    {
        if (anchorRoot != null)
        {
            return;
        }

        var go = new GameObject(anchorRootName);
        anchorRoot = go.transform;
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void EnsureAnchorRoot(Vector3 worldPosition, Quaternion worldRotation)
    {
        EnsureAnchorRoot();
        anchorRoot.SetPositionAndRotation(worldPosition, worldRotation);
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void ClearLocalizationState()
    {
        isLocalized = false;
        currentPlaceId = "";
        if (autoSaveOnChange)
        {
            SaveState();
        }
    }

    public void SaveState()
    {
        var state = new WorldAnchorState
        {
            isLocalized = isLocalized,
            currentPlaceId = currentPlaceId,
            hasAnchorRoot = anchorRoot != null,
            anchorPosition = anchorRoot == null ? null : new[] { anchorRoot.position.x, anchorRoot.position.y, anchorRoot.position.z },
            anchorRotation = anchorRoot == null ? null : new[] { anchorRoot.rotation.x, anchorRoot.rotation.y, anchorRoot.rotation.z, anchorRoot.rotation.w }
        };

        var json = JsonUtility.ToJson(state, true);
        File.WriteAllText(SaveFilePath, json);
    }

    public void LoadState()
    {
        if (!File.Exists(SaveFilePath))
        {
            return;
        }

        var json = File.ReadAllText(SaveFilePath);
        var state = JsonUtility.FromJson<WorldAnchorState>(json);
        if (state == null)
        {
            return;
        }

        isLocalized = state.isLocalized;
        currentPlaceId = state.currentPlaceId ?? "";

        if (state.hasAnchorRoot &&
            state.anchorPosition != null && state.anchorPosition.Length == 3 &&
            state.anchorRotation != null && state.anchorRotation.Length == 4)
        {
            EnsureAnchorRoot();
            var pos = new Vector3(state.anchorPosition[0], state.anchorPosition[1], state.anchorPosition[2]);
            var rot = new Quaternion(state.anchorRotation[0], state.anchorRotation[1], state.anchorRotation[2], state.anchorRotation[3]);
            anchorRoot.SetPositionAndRotation(pos, rot);
        }
    }

    public void DeleteSavedState()
    {
        if (File.Exists(SaveFilePath))
        {
            File.Delete(SaveFilePath);
        }
    }

    public void NotifyLocalized(Transform localizedAnchor, string placeId = "")
    {
        if (localizedAnchor == null)
        {
            SetLocalized(true, placeId);
            return;
        }

        EnsureAnchorRoot(localizedAnchor.position, localizedAnchor.rotation);
        SetLocalized(true, placeId);
    }

    public void NotifyLocalizationLost()
    {
        SetLocalized(false);
    }
}
