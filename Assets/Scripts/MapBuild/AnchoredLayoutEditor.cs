using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class AnchoredLayoutEditor : MonoBehaviour
{
    [Serializable]
    public class LayoutItem
    {
        public string animalId;
        public string prefabName;
        public float px;
        public float py;
        public float pz;
        public float rx;
        public float ry;
        public float rz;
        public float rw;
        public float sx = 1f;
        public float sy = 1f;
        public float sz = 1f;
    }

    [Serializable]
    public class LayoutData
    {
        public string mapId;
        public List<LayoutItem> items = new List<LayoutItem>();
    }

    [Header("Refs")]
    public Transform anchorRoot;
    public Transform anchoredAnimalRoot;
    public TMP_Text statusText;
    public WorldAnchorManager worldAnchorManager;

    [Header("Config")]
    public string targetMapId = "";
    public string layoutFileName = "layouts/AnchoredLayout.json";
    public bool requireLocalized = true;

    public string LayoutFilePath => Path.Combine(Application.persistentDataPath, layoutFileName);

    private void Awake()
    {
        EnsureRoots();
        SetStatus("布局保存器：待命");
    }

    public void SaveLayout()
    {
        if (!CanSaveNow(out string reason))
        {
            SetStatus("保存失败：" + reason);
            return;
        }

        Transform root = anchoredAnimalRoot != null ? anchoredAnimalRoot : anchorRoot;
        if (root == null)
        {
            SetStatus("保存失败：AnchoredAnimalRoot为空");
            return;
        }

        LayoutData data = new LayoutData
        {
            mapId = string.IsNullOrWhiteSpace(targetMapId)
                ? (worldAnchorManager != null ? worldAnchorManager.CurrentPlaceId : "")
                : targetMapId.Trim(),
            items = new List<LayoutItem>()
        };

        for (int i = 0; i < root.childCount; i++)
        {
            Transform t = root.GetChild(i);
            if (t == null)
            {
                continue;
            }

            string prefabName = t.name;
            string animalId = GuessAnimalIdByName(prefabName);

            data.items.Add(new LayoutItem
            {
                animalId = animalId,
                prefabName = prefabName,
                px = t.localPosition.x,
                py = t.localPosition.y,
                pz = t.localPosition.z,
                rx = t.localRotation.x,
                ry = t.localRotation.y,
                rz = t.localRotation.z,
                rw = t.localRotation.w,
                sx = t.localScale.x,
                sy = t.localScale.y,
                sz = t.localScale.z,
            });
        }

        string json = JsonUtility.ToJson(data, true);
        string directory = Path.GetDirectoryName(LayoutFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(LayoutFilePath, json);

        SetStatus($"保存成功：{data.items.Count} 个对象\n{LayoutFilePath}");
        Debug.Log("[AnchoredLayoutEditor] Layout saved: " + LayoutFilePath);
    }

    private bool CanSaveNow(out string reason)
    {
        reason = "";

        if (!requireLocalized)
        {
            return true;
        }

        if (worldAnchorManager == null)
        {
            reason = "worldAnchorManager未绑定";
            return false;
        }

        if (!worldAnchorManager.IsLocalized)
        {
            reason = "当前未Localized";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(targetMapId) &&
            !string.Equals(worldAnchorManager.CurrentPlaceId, targetMapId.Trim(), StringComparison.Ordinal))
        {
            reason = "当前不是目标地图";
            return false;
        }

        return true;
    }

    private void EnsureRoots()
    {
        if (anchorRoot == null)
        {
            GameObject anchor = GameObject.Find("AnchorRoot");
            if (anchor != null)
            {
                anchorRoot = anchor.transform;
            }
        }

        if (anchoredAnimalRoot == null)
        {
            GameObject root = GameObject.Find("AnchoredAnimalRoot");
            if (root != null)
            {
                anchoredAnimalRoot = root.transform;
            }
        }
    }

    private static string GuessAnimalIdByName(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName)) return "";
        string lower = prefabName.ToLowerInvariant();

        if (lower.Contains("bear")) return "bear";
        if (lower.Contains("hare") || lower.Contains("rabbit")) return "hare";
        if (lower.Contains("fox")) return "fox";
        if (lower.Contains("wolf")) return "wolf";
        if (lower.Contains("boar")) return "boar";
        if (lower.Contains("deer")) return "deer";
        if (lower.Contains("moose")) return "moose";

        return "";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log("[AnchoredLayoutEditor] " + message);
    }
}
