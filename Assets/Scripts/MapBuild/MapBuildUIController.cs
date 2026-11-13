using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using easyar;

public class MapBuildUIController : MonoBehaviour
{
    [Header("Refs")]
    public SparseSpatialMapBuilderFrameFilter builder;
    public TMP_Text statusText;
    public Button startBuildButton;
    public Button hostMapButton;

    [Header("Host")]
    public string mapNamePrefix = "ARAnimalMap";

    private bool isBuilding;

    private void Awake()
    {
        BindButtons();
        SetStatus("状态：待命");
    }

    private void OnDestroy()
    {
        if (startBuildButton != null)
        {
            startBuildButton.onClick.RemoveListener(OnClickStartBuild);
        }

        if (hostMapButton != null)
        {
            hostMapButton.onClick.RemoveListener(OnClickHostMap);
        }
    }

    private void BindButtons()
    {
        if (startBuildButton != null)
        {
            startBuildButton.onClick.RemoveListener(OnClickStartBuild);
            startBuildButton.onClick.AddListener(OnClickStartBuild);
        }

        if (hostMapButton != null)
        {
            hostMapButton.onClick.RemoveListener(OnClickHostMap);
            hostMapButton.onClick.AddListener(OnClickHostMap);
        }
    }

    public void OnClickStartBuild()
    {
        if (builder == null)
        {
            SetStatus("状态：失败（Builder 未绑定）");
            Debug.LogError("[MapBuildUIController] builder is null.");
            return;
        }

        bool invoked = TryInvokeNoArgMethod(builder, "Start")
                       || TryInvokeNoArgMethod(builder, "start")
                       || TryInvokeNoArgMethod(builder, "Resume")
                       || TryInvokeNoArgMethod(builder, "resume");

        isBuilding = true;
        if (invoked)
        {
            SetStatus("状态：扫描中");
            Debug.Log("[MapBuildUIController] Build started.");
        }
        else
        {
            SetStatus("状态：扫描中（由AR Session驱动）");
            Debug.Log("[MapBuildUIController] No explicit Start method invoked. Assume session-driven building.");
        }
    }

    public void OnClickHostMap()
    {
        if (builder == null)
        {
            SetStatus("状态：失败（Builder 未绑定）");
            Debug.LogError("[MapBuildUIController] builder is null.");
            return;
        }

        if (!isBuilding)
        {
            SetStatus("状态：提示（建议先开始扫描）");
        }

        string mapName = BuildMapName();
        SetStatus("状态：提交地图中...");

        try
        {
            builder.Host(
                mapName,
                null,
                default(Optional<int>),
                (mapInfoOpt, errorOpt) =>
                {
                    string errorText = ReadOptionalString(errorOpt);
                    if (!string.IsNullOrEmpty(errorText))
                    {
                        SetStatus($"状态：Host失败\n错误：{errorText}");
                        Debug.LogError("[MapBuildUIController] Host failed: " + errorText);
                        return;
                    }

                    string mapId = TryReadMapId(mapInfoOpt);
                    if (string.IsNullOrEmpty(mapId))
                    {
                        SetStatus("状态：Host完成，但未读到MapId");
                        Debug.LogWarning("[MapBuildUIController] Host completed, but MapId is empty.");
                        return;
                    }

                    SetStatus($"状态：建图成功\nMapName: {mapName}\nMapId: {mapId}");
                    Debug.Log($"[MapBuildUIController] Host success. MapName={mapName}, MapId={mapId}");
                }
            );
        }
        catch (Exception ex)
        {
            SetStatus("状态：Host调用异常");
            Debug.LogError("[MapBuildUIController] Host invoke exception: " + ex);
        }
    }

    private string BuildMapName()
    {
        string prefix = string.IsNullOrWhiteSpace(mapNamePrefix) ? "ARAnimalMap" : mapNamePrefix.Trim();
        return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    private static bool TryInvokeNoArgMethod(object target, string methodName)
    {
        if (target == null)
        {
            return false;
        }

        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        if (method == null || method.GetParameters().Length != 0)
        {
            return false;
        }

        try
        {
            method.Invoke(target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadOptionalString(Optional<string> optional)
    {
        try
        {
            string value = (string)optional;
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryReadMapId(object mapInfoOptional)
    {
        try
        {
            if (mapInfoOptional == null)
            {
                return string.Empty;
            }

            object info = mapInfoOptional;
            var optionalType = mapInfoOptional.GetType();
            var valueProp = optionalType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (valueProp != null)
            {
                info = valueProp.GetValue(mapInfoOptional, null);
            }

            if (info == null)
            {
                return string.Empty;
            }

            var infoType = info.GetType();
            foreach (string name in new[] { "ID", "Id", "MapID", "MapId" })
            {
                var prop = infoType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(info, null);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
            }
        }
        catch { }

        return string.Empty;
    }
}
