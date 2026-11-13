using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecognitionStatusUI : MonoBehaviour
{
    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;
    public ExperienceModeCoordinator modeCoordinator;
    public AnchoredLayoutRuntimeLoader layoutLoader;

    public TMP_Text statusText;
    public Slider progressSlider;
    public TMP_Text progressLabel;

    [Header("Options")]
    public bool refreshEveryFrame = true;
    public bool hideWhenAnchoredLoaded = false;

    private bool _layoutLoadedOnce;
    private bool _layoutLoadedFromLoader;

    private void Awake()
    {
        ResolveRefs();
        HookLoader();
    }

    private void Start()
    {
        RefreshNow();
    }

    private void OnDestroy()
    {
        UnhookLoader();
    }

    private void Update()
    {
        if (refreshEveryFrame)
        {
            RefreshNow();
        }
    }

    public void MarkLayoutLoaded()
    {
        _layoutLoadedOnce = true;
        RefreshNow();
    }

    public void RefreshNow()
    {
        ResolveRefs();
        if (!_layoutLoadedOnce && _layoutLoadedFromLoader)
        {
            _layoutLoadedOnce = true;
        }

        string modeText = modeCoordinator != null ? modeCoordinator.CurrentMode.ToString() : "(unknown)";
        bool localized = worldAnchorManager != null && worldAnchorManager.IsLocalized;
        string placeId = worldAnchorManager == null || string.IsNullOrEmpty(worldAnchorManager.CurrentPlaceId)
            ? "(none)"
            : worldAnchorManager.CurrentPlaceId;

        float progress = 0f;
        string phase = "未开始";

        if (!localized)
        {
            progress = 0.1f;
            phase = "扫描环境中";
        }
        else
        {
            progress = 0.7f;
            phase = "已定位";
        }

        if (_layoutLoadedOnce)
        {
            progress = 1f;
            phase = "布局已加载";
        }

        if (hideWhenAnchoredLoaded)
        {
            bool anchoredMode = modeCoordinator != null && modeCoordinator.IsAnchoredExperienceMode;
            if (anchoredMode && _layoutLoadedOnce)
            {
                gameObject.SetActive(false);
                return;
            }
        }

        if (progressSlider != null)
        {
            progressSlider.value = progress;
        }

        if (progressLabel != null)
        {
            progressLabel.text = $"进度：{Mathf.RoundToInt(progress * 100f)}%";
        }

        if (statusText != null)
        {
            statusText.text =
                $"模式：{modeText}\n" +
                $"定位：{(localized ? "已定位" : "未定位")}\n" +
                $"地图ID：{placeId}\n" +
                $"阶段：{phase}";
        }
    }

    private void ResolveRefs()
    {
        if (worldAnchorManager == null)
        {
            worldAnchorManager = FindObjectOfType<WorldAnchorManager>();
        }

        if (modeCoordinator == null)
        {
            modeCoordinator = FindObjectOfType<ExperienceModeCoordinator>();
        }

        if (layoutLoader == null)
        {
            layoutLoader = FindObjectOfType<AnchoredLayoutRuntimeLoader>();
        }

        if (statusText == null)
        {
            var go = GameObject.Find("StatusText");
            if (go != null)
            {
                statusText = go.GetComponent<TMP_Text>();
            }
        }

        if (progressLabel == null)
        {
            var go = GameObject.Find("ProgressLabel");
            if (go != null)
            {
                progressLabel = go.GetComponent<TMP_Text>();
            }
        }

        if (progressSlider == null)
        {
            var go = GameObject.Find("ProgressSlider");
            if (go != null)
            {
                progressSlider = go.GetComponent<Slider>();
            }
        }
    }

    private void HookLoader()
    {
        if (layoutLoader == null)
        {
            return;
        }

        layoutLoader.OnLayoutLoaded -= HandleLayoutLoaded;
        layoutLoader.OnLayoutLoaded += HandleLayoutLoaded;
    }

    private void UnhookLoader()
    {
        if (layoutLoader == null)
        {
            return;
        }

        layoutLoader.OnLayoutLoaded -= HandleLayoutLoaded;
    }

    private void HandleLayoutLoaded(int count)
    {
        _layoutLoadedFromLoader = true;
        _layoutLoadedOnce = true;
        RefreshNow();
    }
}
