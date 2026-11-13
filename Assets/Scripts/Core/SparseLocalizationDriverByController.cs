using System.Reflection;
using UnityEngine;
using easyar;

public class SparseLocalizationDriverByController : MonoBehaviour
{
    [Header("Refs")]
    public SparseSpatialMapController controller;
    public LocalizationBridge bridge;

    [Header("Debug")]
    public bool debugLog = true;
    [Range(0.1f, 2f)]
    public float heartbeatInterval = 0.5f;

    private float nextHeartbeatTime;

    private void Awake()
    {
        if (controller == null)
        {
            controller = FindObjectOfType<SparseSpatialMapController>();
        }

        if (bridge == null)
        {
            bridge = FindObjectOfType<LocalizationBridge>();
        }
    }

    private void OnEnable()
    {
        if (controller == null)
        {
            return;
        }

        controller.TargetFound += OnTargetFound;
        controller.TargetLost += OnTargetLost;
    }

    private void OnDisable()
    {
        if (controller == null)
        {
            return;
        }

        controller.TargetFound -= OnTargetFound;
        controller.TargetLost -= OnTargetLost;
    }

    private void Start()
    {
        nextHeartbeatTime = Time.unscaledTime + heartbeatInterval;
        Log("Driver started.");
    }

    private void Update()
    {
        if (bridge == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextHeartbeatTime)
        {
            nextHeartbeatTime = Time.unscaledTime + heartbeatInterval;
            bridge.OnRecognizingTick();
        }
    }

    private void OnTargetFound()
    {
        if (bridge == null)
        {
            return;
        }

        string mapId = TryReadMapId(controller);
        bridge.OnLocalized(mapId);
        Log("TargetFound. mapId=" + (string.IsNullOrWhiteSpace(mapId) ? "(empty)" : mapId));
    }

    private void OnTargetLost()
    {
        if (bridge == null)
        {
            return;
        }

        bridge.OnLocalizationLost();
        Log("TargetLost.");
    }

    private static string TryReadMapId(SparseSpatialMapController c)
    {
        if (c == null)
        {
            return "";
        }

        object info = c.Info;
        if (info == null)
        {
            return "";
        }

        foreach (string name in new[] { "ID", "Id", "MapID", "MapId", "Name" })
        {
            var prop = info.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                object value = prop.GetValue(info, null);
                if (value != null)
                {
                    string text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            var field = info.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                object value = field.GetValue(info);
                if (value != null)
                {
                    string text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }

        return "";
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log("[SparseLocalizationDriverByController] " + message);
        }
    }
}
