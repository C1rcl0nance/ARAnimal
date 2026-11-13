using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class BundleDownloadProgressHUD : MonoBehaviour
{
    private RemoteAnimalBundleLoader loader;
    private GameObject panel;
    private Slider slider;
    private TMP_Text titleText;
    private TMP_Text percentText;
    private Coroutine autoHideCoroutine;
    private bool bindRoutineStarted;
    private string byteStatusText;

    #if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EnsureEditorHost()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (FindObjectOfType<BundleDownloadProgressHUD>() != null)
        {
            return;
        }

        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        var host = new GameObject("BundleDownloadProgressHUD");
        host.transform.SetParent(canvas.transform, false);
        host.AddComponent<RectTransform>();
        host.AddComponent<BundleDownloadProgressHUD>();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.scene);
    }
    #endif

    private void Awake()
    {
        if (!Application.isPlaying && transform.parent == null)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                transform.SetParent(canvas.transform, false);
            }
        }

        EnsureUI();
        if (Application.isPlaying)
        {
            TryBindLoader();
        }
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
            return;
        }

        if (loader == null && !bindRoutineStarted)
        {
            bindRoutineStarted = true;
            StartCoroutine(BindLoaderRoutine());
        }
    }

    private IEnumerator BindLoaderRoutine()
    {
        while (loader == null)
        {
            TryBindLoader();
            if (loader == null)
            {
                yield return null;
                continue;
            }
        }
    }

    private void OnDestroy()
    {
        if (loader != null)
        {
            loader.BundleDownloadStarted -= OnDownloadStarted;
            loader.BundleDownloadProgress -= OnDownloadProgress;
            loader.BundleDownloadFinished -= OnDownloadFinished;
            loader.BundleDownloadBytes -= OnDownloadBytes;
        }
    }

    private void EnsureUI()
    {
        if (panel == null)
        {
            CleanupAndBindExistingPanel();
        }

        if (panel != null && slider != null && titleText != null && percentText != null)
        {
            ApplyPanelLayout();
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var canvasGo = new GameObject("DownloadProgressCanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        panel = new GameObject("DownloadProgressPanel");
        panel.transform.SetParent(transform, false);
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.65f);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, 80f);
        panelRect.sizeDelta = new Vector2(520f, 100f);

        titleText = CreateText(panel.transform, "Title", new Vector2(0f, 28f), 30f, 26, TextAlignmentOptions.Center, "\u4E0B\u8F7D\u6A21\u578B...");
        percentText = CreateText(panel.transform, "Percent", new Vector2(220f, -4f), 28f, 22, TextAlignmentOptions.Right, "0%");

        var sliderGO = new GameObject("ProgressSlider");
        sliderGO.transform.SetParent(panel.transform, false);
        slider = sliderGO.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        var sliderRect = sliderGO.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(0f, -10f);
        sliderRect.sizeDelta = new Vector2(440f, 20f);

        var bg = new GameObject("Background");
        bg.transform.SetParent(sliderGO.transform, false);
        var bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(1f, 1f, 1f, 0.2f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGO.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(5f, 5f);
        fillAreaRect.offsetMax = new Vector2(-5f, -5f);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.25f, 0.8f, 0.35f, 1f);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        slider.targetGraphic = fillImage;
        slider.fillRect = fillRect;

        panel.SetActive(false);
        ApplyPanelLayout();

        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
        #endif
    }

    private void CleanupAndBindExistingPanel()
    {
        var children = transform.GetComponentsInChildren<Transform>(true);
        var foundPanels = new List<GameObject>();
        for (int index = 0; index < children.Length; index++)
        {
            var item = children[index];
            if (item == null || item.parent != transform)
            {
                continue;
            }

            if (item.name == "DownloadProgressPanel")
            {
                foundPanels.Add(item.gameObject);
            }
        }

        if (foundPanels.Count == 0)
        {
            return;
        }

        panel = foundPanels[0];
        for (int index = 1; index < foundPanels.Count; index++)
        {
            SafeDestroy(foundPanels[index]);
        }

        var sliderTf = panel.transform.Find("ProgressSlider");
        slider = sliderTf != null ? sliderTf.GetComponent<Slider>() : null;
        var titleTf = panel.transform.Find("Title");
        titleText = titleTf != null ? titleTf.GetComponent<TMP_Text>() : null;
        var percentTf = panel.transform.Find("Percent");
        percentText = percentTf != null ? percentTf.GetComponent<TMP_Text>() : null;
        ApplyPanelLayout();
    }

    private void ApplyPanelLayout()
    {
        if (panel == null)
        {
            return;
        }

        var panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null)
        {
            return;
        }

        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, 80f);
        panelRect.sizeDelta = new Vector2(520f, 100f);
    }

    private static void SafeDestroy(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            #if UNITY_EDITOR
            DestroyImmediate(target);
            #endif
        }
    }

    private void TryBindLoader()
    {
        if (loader != null)
        {
            return;
        }

        loader = FindObjectOfType<RemoteAnimalBundleLoader>();
        if (loader == null)
        {
            return;
        }

        loader.BundleDownloadStarted += OnDownloadStarted;
        loader.BundleDownloadProgress += OnDownloadProgress;
        loader.BundleDownloadFinished += OnDownloadFinished;
        loader.BundleDownloadBytes += OnDownloadBytes;
    }

    private TMP_Text CreateText(Transform parent, string name, Vector2 anchoredPos, float height, float fontSize, TextAlignmentOptions alignment, string initialText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = initialText;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(460f, height);
        return text;
    }

    private void OnDownloadStarted(string bundleName)
    {
        EnsureUI();
        if (autoHideCoroutine != null)
        {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }

        panel.SetActive(true);
        slider.value = 0f;
        titleText.text = $"\u6B63\u5728\u4E0B\u8F7D\uFF1A{bundleName}";
        byteStatusText = "\u5DF2\u4E0B\u8F7D 0 B";
        percentText.text = byteStatusText;
    }

    private void OnDownloadProgress(string bundleName, float progress)
    {
        EnsureUI();
        if (!panel.activeSelf)
        {
            panel.SetActive(true);
        }

        if (progress < 0f)
        {
            slider.value = Mathf.PingPong(Time.unscaledTime * 0.6f, 0.9f);
            percentText.text = string.IsNullOrWhiteSpace(byteStatusText) ? "\u4E0B\u8F7D\u4E2D..." : byteStatusText;
            return;
        }

        slider.value = Mathf.Clamp01(progress);
        percentText.text = string.IsNullOrWhiteSpace(byteStatusText)
            ? $"{Mathf.RoundToInt(slider.value * 100f)}%"
            : byteStatusText;
    }

    private void OnDownloadFinished(string bundleName, bool success, string reason)
    {
        EnsureUI();
        slider.value = success ? 1f : slider.value;
        if (success && !string.IsNullOrWhiteSpace(byteStatusText))
        {
            percentText.text = byteStatusText;
        }
        else
        {
            percentText.text = success ? "100%" : percentText.text;
        }
        titleText.text = success
            ? $"\u5DF2\u5B8C\u6210\uFF1A{bundleName}"
            : $"\u4E0B\u8F7D\u5931\u8D25\uFF1A{bundleName}";

        if (success)
        {
            autoHideCoroutine = StartCoroutine(HideAfterDelay(0.8f));
        }
        else
        {
            Debug.LogWarning($"[BundleDownloadProgressHUD] Download failed: {bundleName}, reason={reason}");
        }
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void OnDownloadBytes(string bundleName, ulong downloadedBytes, ulong totalBytes)
    {
        if (totalBytes > 0)
        {
            byteStatusText = $"\u5DF2\u4E0B\u8F7D {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}";
        }
        else
        {
            byteStatusText = $"\u5DF2\u4E0B\u8F7D {FormatBytes(downloadedBytes)}";
        }

        if (panel != null && panel.activeSelf && percentText != null)
        {
            percentText.text = byteStatusText;
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        const double kb = 1024d;
        const double mb = 1024d * 1024d;
        const double gb = 1024d * 1024d * 1024d;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.00} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.00} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.00} KB";
        }

        return $"{bytes} B";
    }
}
