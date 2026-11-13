using UnityEngine;

public class LocalizationBridge : MonoBehaviour
{
    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;
    public Transform localizedAnchorTransform;

    [Header("Config")]
    public string targetMapId = "";

    [Header("Debug")]
    public bool debugLog = true;
    public bool heartbeatLog = true;
    [Range(1f, 10f)] public float heartbeatIntervalSeconds = 2f;
    [Range(2f, 30f)] public float staleCallbackWarningSeconds = 8f;

    private float _nextHeartbeatTime;
    private float _lastCallbackTime = -1f;
    private string _lastCallbackName = "none";

    private void Awake()
    {
        if (worldAnchorManager == null)
        {
            worldAnchorManager = FindObjectOfType<WorldAnchorManager>();
        }

        if (localizedAnchorTransform == null)
        {
            var anchorRoot = GameObject.Find("AnchorRoot");
            if (anchorRoot != null)
            {
                localizedAnchorTransform = anchorRoot.transform;
            }
        }
    }

    private void Start()
    {
        _nextHeartbeatTime = Time.unscaledTime + heartbeatIntervalSeconds;
        Log("Bridge started. Waiting recognition callbacks...");
    }

    private void Update()
    {
        if (!heartbeatLog || !debugLog)
        {
            return;
        }

        if (Time.unscaledTime < _nextHeartbeatTime)
        {
            return;
        }

        _nextHeartbeatTime = Time.unscaledTime + heartbeatIntervalSeconds;

        bool localized = worldAnchorManager != null && worldAnchorManager.IsLocalized;
        string placeId = worldAnchorManager == null || string.IsNullOrEmpty(worldAnchorManager.CurrentPlaceId)
            ? "(none)"
            : worldAnchorManager.CurrentPlaceId;

        string staleHint = "";
        if (_lastCallbackTime < 0f)
        {
            staleHint = " | callback=never";
        }
        else
        {
            float elapsed = Time.unscaledTime - _lastCallbackTime;
            staleHint = $" | callback={_lastCallbackName} {elapsed:F1}s ago";
            if (elapsed >= staleCallbackWarningSeconds)
            {
                staleHint += " (STALE)";
            }
        }

        Log($"Heartbeat: localized={localized}, placeId={placeId}{staleHint}");
    }

    /// <summary>
    /// 给识别成功回调调用（推荐）
    /// </summary>
    public void OnLocalized(string mapId)
    {
        if (worldAnchorManager == null)
        {
            LogError("OnLocalized failed: worldAnchorManager is null");
            return;
        }

        string finalMapId = string.IsNullOrWhiteSpace(mapId) ? targetMapId : mapId.Trim();
        worldAnchorManager.NotifyLocalized(localizedAnchorTransform, finalMapId);
        StampCallback("OnLocalized");

        Log($"Localized success. mapId={finalMapId}");
    }

    /// <summary>
    /// 给识别丢失回调调用
    /// </summary>
    public void OnLocalizationLost()
    {
        if (worldAnchorManager == null)
        {
            LogError("OnLocalizationLost failed: worldAnchorManager is null");
            return;
        }

        worldAnchorManager.NotifyLocalizationLost();
        StampCallback("OnLocalizationLost");
        Log("Localization lost.");
    }

    /// <summary>
    /// 可选：在“识别流程开始尝试”时调用，用于诊断“是否真的在识别”
    /// </summary>
    public void OnRecognizingTick()
    {
        StampCallback("OnRecognizingTick");
        Log("Recognizing tick.");
    }

    /// <summary>
    /// 临时测试按钮：手动设为“已识别”
    /// </summary>
    public void DebugSetLocalized()
    {
        string mapId = string.IsNullOrWhiteSpace(targetMapId) ? "(debug-map)" : targetMapId.Trim();
        OnLocalized(mapId);
    }

    /// <summary>
    /// 临时测试按钮：手动设为“未识别”
    /// </summary>
    public void DebugSetLost()
    {
        OnLocalizationLost();
    }

    private void StampCallback(string callbackName)
    {
        _lastCallbackName = callbackName;
        _lastCallbackTime = Time.unscaledTime;
    }

    private void Log(string msg)
    {
        if (debugLog)
        {
            Debug.Log("[LocalizationBridge] " + msg);
        }
    }

    private void LogError(string msg)
    {
        Debug.LogError("[LocalizationBridge] " + msg);
    }
}
