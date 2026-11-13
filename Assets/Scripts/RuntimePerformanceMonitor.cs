using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

/// <summary>
/// Runtime performance monitor:
/// 1) shows FPS on-screen
/// 2) logs app memory usage periodically
/// </summary>
public sealed class RuntimePerformanceMonitor : MonoBehaviour
{
    [SerializeField] private float memoryLogIntervalSeconds = 2f;
    [SerializeField] private int uiFontSize = 30;

    private Text fpsText;
    private float smoothedDeltaTime;
    private float nextMemoryLogTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<RuntimePerformanceMonitor>() != null)
        {
            return;
        }

        var root = new GameObject("RuntimePerformanceMonitor");
        DontDestroyOnLoad(root);
        root.AddComponent<RuntimePerformanceMonitor>();
    }

    private void Awake()
    {
        CreateFpsUi();
        nextMemoryLogTime = Time.unscaledTime + memoryLogIntervalSeconds;
    }

    private void Update()
    {
        UpdateFpsUi();
        TryLogMemoryUsage();
    }

    private void CreateFpsUi()
    {
        var canvasObj = new GameObject("PerfCanvas");
        canvasObj.transform.SetParent(transform, false);

        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var textObj = new GameObject("FpsText");
        textObj.transform.SetParent(canvasObj.transform, false);

        fpsText = textObj.AddComponent<Text>();
        fpsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        fpsText.fontSize = uiFontSize;
        fpsText.alignment = TextAnchor.UpperLeft;
        fpsText.horizontalOverflow = HorizontalWrapMode.Overflow;
        fpsText.verticalOverflow = VerticalWrapMode.Overflow;
        fpsText.color = Color.white;

        var rect = fpsText.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -20f);
        rect.sizeDelta = new Vector2(360f, 80f);
    }

    private void UpdateFpsUi()
    {
        smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
        var fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
        fpsText.text = $"FPS: {fps:0.0}";
    }

    private void TryLogMemoryUsage()
    {
        if (Time.unscaledTime < nextMemoryLogTime)
        {
            return;
        }

        nextMemoryLogTime = Time.unscaledTime + memoryLogIntervalSeconds;

        // Unity process memory counters
        var allocatedMB = BytesToMB(Profiler.GetTotalAllocatedMemoryLong());
        var reservedMB = BytesToMB(Profiler.GetTotalReservedMemoryLong());
        var monoMB = BytesToMB(Profiler.GetMonoUsedSizeLong());

        Debug.Log(
            $"[Perf] FPS={GetCurrentFps():0.0}, " +
            $"Allocated={allocatedMB:0.00}MB, Reserved={reservedMB:0.00}MB, MonoUsed={monoMB:0.00}MB");
    }

    private float GetCurrentFps()
    {
        return smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
    }

    private static float BytesToMB(long bytes)
    {
        return bytes / (1024f * 1024f);
    }
}

