using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class AnimalSelectionPanelController : MonoBehaviour
{
    [Serializable]
    private class AnimalCatalogResponse
    {
        public List<AnimalTypeConfig> animals;
        public List<AnimalTypeConfig> data;
    }

    [Serializable]
    public class AnimalVariantConfig
    {
        public string variantId;
        public string displayName;
        public string prefabName;
    }

    [Serializable]
    public class AnimalTypeConfig
    {
        public string animalId;
        public string displayName;
        public string bundleName;
        public string bundleUrl;
        public List<AnimalVariantConfig> variants = new List<AnimalVariantConfig>();
    }

    [Header("Refs")]
    public TileAnimalFlowController flowController;
    public AnimalPickerUI animalPickerUI;
    public GameObject typePanelRoot;
    public GameObject variantPanelRoot;
    public Transform typeContent;
    public Transform variantContent;
    public Button openPickerButton;
    public Button clearSelectionButtonShort;
    public Button variantButtonTemplate;
    public TMP_Text currentAnimalTextShort;
    public RectTransform bottomRightActionPanel;
    public TMP_Text selectedFootprintText;
    public RemoteAnimalBundleLoader remoteBundleLoader;
    public AutoPlaneAreaEstimator planeEstimator;

    [Header("Configs")]
    public List<AnimalTypeConfig> animalTypes = new List<AnimalTypeConfig>();

    [Header("Behavior")]
    public bool autoCloseAfterVariantSelect = true;
    public string summaryPrefix = "当前：";

    [Header("Catalog API")]
    public bool loadCatalogFromApi = true;
    public string catalogApiUrl = "http://115.190.8.234/ARAnimal-api/api/animals/catalog";
    [Range(3, 60)] public int catalogApiTimeoutSeconds = 12;
    public bool debugCatalogLog = true;

    [Header("Footprint Fit Check")]
    [Tooltip("严格按长宽方向比较：模型长<=平面长 且 模型宽<=平面宽 才可放置。")]
    public bool strictLengthWidthFit = true;

    [Header("Placement Block Popup")]
    public GameObject placementBlockedPopupRoot;
    public TMP_Text placementBlockedPopupText;
    [Range(0.2f, 5f)] public float placementBlockedPopupDuration = 2f;

    private readonly Dictionary<string, AnimalTypeConfig> typeMap = new Dictionary<string, AnimalTypeConfig>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Button> spawnedVariantButtons = new List<Button>();
    private readonly List<Button> managedTypeButtons = new List<Button>();
    private string selectedTypeId = "";
    private bool hasLastMeasuredFootprint = false;
    private float lastMeasuredWidthMeters = 0f;
    private float lastMeasuredLengthMeters = 0f;
    private string lastMeasuredDisplayName = "";
    private CanvasGroup placementBlockedPopupCanvasGroup;
    private Coroutine placementBlockedPopupRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<AnimalSelectionPanelController>() != null)
        {
            return;
        }

        var host = GameObject.Find("AnimalTypePanel");
        if (host == null)
        {
            return;
        }

        host.AddComponent<AnimalSelectionPanelController>();
    }

    private void Awake()
    {
        ResolveRefs();
        BuildDefaultConfigsIfNeeded();
        RebuildTypeMap();
        BindOpenButton();
        BindTypeButtons();
        SetPanelVisible(false);
    }

    private void Start()
    {
        if (loadCatalogFromApi && !string.IsNullOrWhiteSpace(catalogApiUrl))
        {
            StartCoroutine(FetchCatalogFromApiRoutine(catalogApiUrl.Trim()));
        }
    }

    private void OnDestroy()
    {
        if (openPickerButton != null)
        {
            openPickerButton.onClick.RemoveListener(OpenPanel);
        }
    }

    private void Update()
    {
        if (!hasLastMeasuredFootprint || selectedFootprintText == null)
        {
            return;
        }

        selectedFootprintText.text = BuildFootprintDisplayText(lastMeasuredLengthMeters, lastMeasuredWidthMeters, lastMeasuredDisplayName);
    }

    public void OpenPanel()
    {
        if (typePanelRoot != null)
        {
            typePanelRoot.SetActive(true);
        }

        if (variantPanelRoot != null)
        {
            variantPanelRoot.SetActive(!string.IsNullOrEmpty(selectedTypeId));
        }
    }

    public void ClosePanel()
    {
        SetPanelVisible(false);
    }

    public void SelectType(string animalId)
    {
        if (string.IsNullOrWhiteSpace(animalId))
        {
            return;
        }

        string normalized = animalId.Trim();
        if (!typeMap.TryGetValue(normalized, out var config))
        {
            return;
        }

        selectedTypeId = normalized;
        BuildVariantButtons(config);

        if (variantPanelRoot != null)
        {
            variantPanelRoot.SetActive(true);
        }
    }

    public void SelectVariant(string animalId, string variantPrefabName)
    {
        if (string.IsNullOrWhiteSpace(animalId) || string.IsNullOrWhiteSpace(variantPrefabName))
        {
            return;
        }

        string normalizedId = animalId.Trim();
        if (!typeMap.TryGetValue(normalizedId, out var typeConfig))
        {
            return;
        }

        var displayName = typeConfig.displayName;
        for (int index = 0; index < typeConfig.variants.Count; index++)
        {
            var candidate = typeConfig.variants[index];
            if (candidate != null && string.Equals(candidate.prefabName, variantPrefabName, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(candidate.displayName))
                {
                    displayName = $"{displayName} - {candidate.displayName}";
                }
                break;
            }
        }

        flowController?.SelectAnimal(normalizedId);
        flowController?.SetRuntimeRemoteVariant(normalizedId, typeConfig.bundleName, typeConfig.bundleUrl, variantPrefabName.Trim());
        animalPickerUI?.SelectAnimal(normalizedId);
        bool startedPreload = TryPreloadBundleIfNeeded(typeConfig, displayName);
        if (!startedPreload)
        {
            ApplySummaryText(displayName);
        }
        StartCoroutine(UpdateSelectedVariantFootprintRoutine(typeConfig, variantPrefabName.Trim(), displayName));

        if (autoCloseAfterVariantSelect)
        {
            ClosePanel();
        }
    }

    private void ResolveRefs()
    {
        if (flowController == null)
        {
            flowController = FindObjectOfType<TileAnimalFlowController>();
        }

        if (animalPickerUI == null)
        {
            animalPickerUI = FindObjectOfType<AnimalPickerUI>();
        }

        if (typePanelRoot == null)
        {
            var go = GameObject.Find("AnimalTypePanel");
            if (go != null)
            {
                typePanelRoot = go;
            }
        }

        if (variantPanelRoot == null)
        {
            var go = GameObject.Find("AnimalVariantPanel");
            if (go != null)
            {
                variantPanelRoot = go;
            }
        }

        if (typeContent == null)
        {
            var go = GameObject.Find("TypeContent");
            if (go != null)
            {
                typeContent = go.transform;
            }
        }

        if (variantContent == null)
        {
            var go = GameObject.Find("VariantContent");
            if (go != null)
            {
                variantContent = go.transform;
            }
        }

        if (openPickerButton == null)
        {
            var go = GameObject.Find("OpenPickerButton");
            if (go != null)
            {
                openPickerButton = go.GetComponent<Button>();
            }
        }

        if (variantButtonTemplate == null && variantContent != null)
        {
            var template = variantContent.Find("VariantButtonTemplate");
            if (template != null)
            {
                variantButtonTemplate = template.GetComponent<Button>();
            }
        }

        if (currentAnimalTextShort == null)
        {
            var go = GameObject.Find("CurrentAnimalText_Short");
            if (go != null)
            {
                currentAnimalTextShort = go.GetComponent<TMP_Text>();
            }
        }

        if (clearSelectionButtonShort == null)
        {
            var go = GameObject.Find("ClearSelectionButton_Short");
            if (go != null)
            {
                clearSelectionButtonShort = go.GetComponent<Button>();
            }
        }

        if (bottomRightActionPanel == null)
        {
            var go = GameObject.Find("BottomRightActionPanel");
            if (go != null)
            {
                bottomRightActionPanel = go.GetComponent<RectTransform>();
            }
        }

        if (remoteBundleLoader == null)
        {
            remoteBundleLoader = FindObjectOfType<RemoteAnimalBundleLoader>();
        }
        if (planeEstimator == null)
        {
            planeEstimator = FindObjectOfType<AutoPlaneAreaEstimator>();
        }
        if (selectedFootprintText == null)
        {
            var go = GameObject.Find("SelectedAnimalFootprintText");
            if (go != null)
            {
                selectedFootprintText = go.GetComponent<TMP_Text>();
            }
        }
        if (placementBlockedPopupRoot == null)
        {
            var go = GameObject.Find("PlacementBlockedPopup");
            if (go != null)
            {
                placementBlockedPopupRoot = go;
            }
        }
        if (placementBlockedPopupText == null && placementBlockedPopupRoot != null)
        {
            placementBlockedPopupText = placementBlockedPopupRoot.GetComponentInChildren<TMP_Text>(true);
        }
        if (placementBlockedPopupRoot != null && placementBlockedPopupCanvasGroup == null)
        {
            placementBlockedPopupCanvasGroup = placementBlockedPopupRoot.GetComponent<CanvasGroup>();
            if (placementBlockedPopupCanvasGroup == null)
            {
                placementBlockedPopupCanvasGroup = placementBlockedPopupRoot.AddComponent<CanvasGroup>();
            }
            placementBlockedPopupCanvasGroup.alpha = 0f;
            placementBlockedPopupRoot.SetActive(false);
        }

        if (flowController != null && flowController.animalSelectionPanelController == null)
        {
            flowController.animalSelectionPanelController = this;
        }
    }

    private void BuildDefaultConfigsIfNeeded()
    {
        if (animalTypes != null && animalTypes.Count > 0)
        {
            return;
        }

        animalTypes = new List<AnimalTypeConfig>
        {
            BuildType("bear", "熊", "bear_bundle", "http://115.190.8.234/bundles/android/bear_bundle", "Bear_cub_LowPoly_IP", "Bear_male_LowPoly_IP"),
            BuildType("boar", "野猪", "boar_bundle", "http://115.190.8.234/bundles/android/boar_bundle", "Boar_cub_Lowpoly_IP", "Boar_Female_Mobile_IP", "Boar_Male_Lowpoly_IP"),
            BuildType("deer", "鹿", "deer_bundle", "http://115.190.8.234/bundles/android/deer_bundle", "Deer_calf_LowPoly_IP", "Deer_doe_LowPoly_IP", "Deer_red_LowPoly_IP", "Deer_stag_LowPoly_IP"),
            BuildType("fox", "狐狸", "fox_bundle", "http://115.190.8.234/bundles/android/fox_bundle", "Fox_cub_LowPoly_IP", "Fox_male_LowPoly_IP"),
            BuildType("hare", "野兔", "hare_bundle", "http://115.190.8.234/bundles/android/hare_bundle", "Hare_cub_LowPoly_IP", "Hare_male_LowPoly_IP"),
            BuildType("moose", "驼鹿", "moose_bundle", "http://115.190.8.234/bundles/android/moose_bundle", "Moose_bull_LowPoly_IP", "Moose_calf_LowPoly_IP", "Moose_cow_LowPoly_IP"),
            BuildType("wolf", "狼", "wolf_bundle", "http://115.190.8.234/bundles/android/wolf_bundle", "Wolf_cub_LowPoly_IP"),
        };

        if (flowController != null && flowController.animalPrefabs != null && flowController.animalPrefabs.Count > 0)
        {
            for (int index = 0; index < flowController.animalPrefabs.Count; index++)
            {
                var entry = flowController.animalPrefabs[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.animalId))
                {
                    continue;
                }

                for (int typeIndex = 0; typeIndex < animalTypes.Count; typeIndex++)
                {
                    var type = animalTypes[typeIndex];
                    if (string.Equals(type.animalId, entry.animalId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(entry.remoteBundleName))
                        {
                            type.bundleName = entry.remoteBundleName.Trim();
                        }

                        if (!string.IsNullOrWhiteSpace(entry.remoteBundleUrl))
                        {
                            type.bundleUrl = entry.remoteBundleUrl.Trim();
                        }
                    }
                }
            }
        }
    }

    private static AnimalTypeConfig BuildType(string animalId, string displayName, string bundleName, string bundleUrl, params string[] prefabNames)
    {
        var type = new AnimalTypeConfig
        {
            animalId = animalId,
            displayName = displayName,
            bundleName = bundleName,
            bundleUrl = bundleUrl,
            variants = new List<AnimalVariantConfig>(),
        };

        for (int index = 0; index < prefabNames.Length; index++)
        {
            var prefabName = prefabNames[index];
            type.variants.Add(new AnimalVariantConfig
            {
                variantId = $"{animalId}_{index + 1}",
                displayName = prefabName,
                prefabName = prefabName,
            });
        }

        return type;
    }

    private void RebuildTypeMap()
    {
        typeMap.Clear();
        if (animalTypes == null)
        {
            return;
        }

        for (int index = 0; index < animalTypes.Count; index++)
        {
            var type = animalTypes[index];
            if (type == null || string.IsNullOrWhiteSpace(type.animalId))
            {
                continue;
            }

            string id = type.animalId.Trim();
            if (!typeMap.ContainsKey(id))
            {
                typeMap.Add(id, type);
            }
        }
    }

    private void BindOpenButton()
    {
        if (openPickerButton == null)
        {
            return;
        }

        openPickerButton.onClick.RemoveListener(OpenPanel);
        openPickerButton.onClick.AddListener(OpenPanel);
    }

    private void BindTypeButtons()
    {
        managedTypeButtons.Clear();
        if (typeContent == null)
        {
            return;
        }

        for (int index = 0; index < animalTypes.Count; index++)
        {
            var type = animalTypes[index];
            if (type == null || string.IsNullOrWhiteSpace(type.animalId))
            {
                continue;
            }

            Button button = FindTypeButton(type.animalId.Trim());
            if (button == null)
            {
                continue;
            }

            managedTypeButtons.Add(button);
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();

            string capturedAnimalId = type.animalId.Trim();
            button.onClick.AddListener(() => SelectType(capturedAnimalId));
            SetButtonLabel(button, type.displayName);
        }
    }

    private Button FindTypeButton(string animalId)
    {
        var target = typeContent.Find("TypeButton_" + animalId);
        if (target != null)
        {
            return target.GetComponent<Button>();
        }

        for (int index = 0; index < typeContent.childCount; index++)
        {
            var child = typeContent.GetChild(index);
            if (child == null || child.name.IndexOf("TypeButton_", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            return child.GetComponent<Button>();
        }

        return null;
    }

    private void BuildVariantButtons(AnimalTypeConfig type)
    {
        ClearSpawnedVariantButtons();
        if (variantContent == null || variantButtonTemplate == null || type == null || type.variants == null)
        {
            return;
        }

        variantButtonTemplate.gameObject.SetActive(false);

        for (int index = 0; index < type.variants.Count; index++)
        {
            var variant = type.variants[index];
            if (variant == null || string.IsNullOrWhiteSpace(variant.prefabName))
            {
                continue;
            }

            var button = Instantiate(variantButtonTemplate, variantContent);
            button.gameObject.SetActive(true);
            button.name = $"VariantButton_{type.animalId}_{index + 1}";
            button.onClick.RemoveAllListeners();

            string capturedAnimalId = type.animalId.Trim();
            string capturedPrefab = variant.prefabName.Trim();
            button.onClick.AddListener(() => SelectVariant(capturedAnimalId, capturedPrefab));

            SetButtonLabel(button, string.IsNullOrWhiteSpace(variant.displayName) ? variant.prefabName.Trim() : variant.displayName.Trim());
            spawnedVariantButtons.Add(button);
        }
    }

    private void ClearSpawnedVariantButtons()
    {
        for (int index = 0; index < spawnedVariantButtons.Count; index++)
        {
            var button = spawnedVariantButtons[index];
            if (button != null)
            {
                Destroy(button.gameObject);
            }
        }

        spawnedVariantButtons.Clear();
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
        {
            return;
        }

        var text = button.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.text = label;
        }
    }

    private void ApplySummaryText(string content)
    {
        if (currentAnimalTextShort == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            currentAnimalTextShort.text = $"{summaryPrefix}未选择";
            return;
        }

        currentAnimalTextShort.text = $"{summaryPrefix}{content.Trim()}";
    }

    private void SetPanelVisible(bool visible)
    {
        if (typePanelRoot != null)
        {
            typePanelRoot.SetActive(visible);
        }

        if (variantPanelRoot != null)
        {
            variantPanelRoot.SetActive(visible && !string.IsNullOrEmpty(selectedTypeId));
        }
    }

    private bool TryPreloadBundleIfNeeded(AnimalTypeConfig typeConfig, string displayName)
    {
        if (typeConfig == null || remoteBundleLoader == null)
        {
            return false;
        }

        if (remoteBundleLoader.IsBundleCached(typeConfig.bundleName, typeConfig.bundleUrl))
        {
            return false;
        }

        if (currentAnimalTextShort != null)
        {
            currentAnimalTextShort.text = $"{summaryPrefix}{displayName} (需要下载)";
        }

        remoteBundleLoader.PreloadBundle(typeConfig.bundleName, typeConfig.bundleUrl, success =>
        {
            if (success)
            {
                ApplySummaryText(displayName);
                return;
            }

            if (currentAnimalTextShort != null)
            {
                currentAnimalTextShort.text = $"{summaryPrefix}{displayName} (下载失败)";
            }
        });
        return true;
    }

    private IEnumerator FetchCatalogFromApiRoutine(string url)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = catalogApiTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogCatalog($"Catalog API failed, fallback local. url={url}, code={request.responseCode}, error={request.error}");
                yield break;
            }

            string json = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (string.IsNullOrWhiteSpace(json))
            {
                LogCatalog($"Catalog API empty payload, fallback local. url={url}");
                yield break;
            }

            if (!TryParseCatalog(json, out List<AnimalTypeConfig> remoteTypes))
            {
                LogCatalog($"Catalog parse failed, fallback local. url={url}");
                yield break;
            }

            if (remoteTypes == null || remoteTypes.Count == 0)
            {
                LogCatalog($"Catalog list empty, fallback local. url={url}");
                yield break;
            }

            animalTypes = remoteTypes;
            RebuildTypeMap();
            BindTypeButtons();

            if (!string.IsNullOrWhiteSpace(selectedTypeId) && typeMap.TryGetValue(selectedTypeId, out var selectedType))
            {
                BuildVariantButtons(selectedType);
            }

            LogCatalog($"Catalog loaded from API. url={url}, count={animalTypes.Count}");
        }
    }

    private bool TryParseCatalog(string rawJson, out List<AnimalTypeConfig> list)
    {
        list = null;
        string json = rawJson.Trim();

        if (json.StartsWith("["))
        {
            json = "{\"animals\":" + json + "}";
        }

        AnimalCatalogResponse response;
        try
        {
            response = JsonUtility.FromJson<AnimalCatalogResponse>(json);
        }
        catch (Exception exception)
        {
            LogCatalog("Catalog parse exception: " + exception.Message);
            return false;
        }

        if (response == null)
        {
            return false;
        }

        List<AnimalTypeConfig> source = response.animals;
        if (source == null || source.Count == 0)
        {
            source = response.data;
        }

        if (source == null || source.Count == 0)
        {
            return false;
        }

        var normalized = new List<AnimalTypeConfig>();
        for (int i = 0; i < source.Count; i++)
        {
            var type = source[i];
            if (type == null || string.IsNullOrWhiteSpace(type.animalId))
            {
                continue;
            }

            type.animalId = type.animalId.Trim();
            type.displayName = string.IsNullOrWhiteSpace(type.displayName) ? type.animalId : type.displayName.Trim();
            type.bundleName = string.IsNullOrWhiteSpace(type.bundleName) ? "" : type.bundleName.Trim();
            type.bundleUrl = string.IsNullOrWhiteSpace(type.bundleUrl) ? "" : type.bundleUrl.Trim();

            var variants = new List<AnimalVariantConfig>();
            if (type.variants != null)
            {
                for (int vi = 0; vi < type.variants.Count; vi++)
                {
                    var variant = type.variants[vi];
                    if (variant == null || string.IsNullOrWhiteSpace(variant.prefabName))
                    {
                        continue;
                    }

                    variant.prefabName = variant.prefabName.Trim();
                    variant.variantId = string.IsNullOrWhiteSpace(variant.variantId) ? $"{type.animalId}_{vi + 1}" : variant.variantId.Trim();
                    variant.displayName = string.IsNullOrWhiteSpace(variant.displayName) ? variant.prefabName : variant.displayName.Trim();
                    variants.Add(variant);
                }
            }

            type.variants = variants;
            if (type.variants.Count > 0)
            {
                normalized.Add(type);
            }
        }

        if (normalized.Count == 0)
        {
            return false;
        }

        list = normalized;
        return true;
    }

    private void LogCatalog(string message)
    {
        if (debugCatalogLog)
        {
            Debug.Log("[AnimalSelectionPanelController] " + message);
        }
    }

    private IEnumerator UpdateSelectedVariantFootprintRoutine(AnimalTypeConfig typeConfig, string prefabName, string displayName)
    {
        if (selectedFootprintText != null)
        {
            selectedFootprintText.text = $"模型占地：计算中（{displayName}）";
        }

        if (typeConfig == null || string.IsNullOrWhiteSpace(prefabName) || remoteBundleLoader == null)
        {
            if (selectedFootprintText != null)
            {
                selectedFootprintText.text = $"模型占地：未知（{displayName}）";
            }
            yield break;
        }

        bool finished = false;
        GameObject loadedPrefab = null;
        remoteBundleLoader.LoadAnimalPrefab(typeConfig.bundleName, typeConfig.bundleUrl, prefabName, prefab =>
        {
            loadedPrefab = prefab;
            finished = true;
        });

        while (!finished)
        {
            yield return null;
        }

        if (loadedPrefab == null)
        {
            if (selectedFootprintText != null)
            {
                selectedFootprintText.text = $"模型占地：加载失败（{displayName}）";
            }
            yield break;
        }

        if (!TryMeasureFootprint(loadedPrefab, out float widthMeters, out float lengthMeters))
        {
            if (selectedFootprintText != null)
            {
                selectedFootprintText.text = $"模型占地：无网格数据（{displayName}）";
            }
            yield break;
        }

        hasLastMeasuredFootprint = true;
        lastMeasuredWidthMeters = widthMeters;
        lastMeasuredLengthMeters = lengthMeters;
        lastMeasuredDisplayName = displayName ?? "";

        if (selectedFootprintText != null)
        {
            selectedFootprintText.text = BuildFootprintDisplayText(lengthMeters, widthMeters, displayName);
        }
    }

    private static bool TryMeasureFootprint(GameObject prefab, out float widthMeters, out float lengthMeters)
    {
        widthMeters = 0f;
        lengthMeters = 0f;
        if (prefab == null)
        {
            return false;
        }

        GameObject temp = null;
        try
        {
            temp = Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.SetActive(false);

            var renderers = temp.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            widthMeters = Mathf.Abs(bounds.size.x);
            lengthMeters = Mathf.Abs(bounds.size.z);

            if (widthMeters <= 0.0001f || lengthMeters <= 0.0001f)
            {
                return false;
            }

            return true;
        }
        finally
        {
            if (temp != null)
            {
                Destroy(temp);
            }
        }
    }

    private string BuildFootprintDisplayText(float lengthMeters, float widthMeters, string displayName)
    {
        string fitText = "";
        if (planeEstimator != null)
        {
            float planeLength = planeEstimator.CurrentPlaneLength;
            float planeWidth = planeEstimator.CurrentPlaneWidth;
            if (planeLength > 0f && planeWidth > 0f)
            {
                bool fit;
                if (strictLengthWidthFit)
                {
                    fit = lengthMeters <= planeLength && widthMeters <= planeWidth;
                }
                else
                {
                    fit = (lengthMeters <= planeLength && widthMeters <= planeWidth) ||
                          (lengthMeters <= planeWidth && widthMeters <= planeLength);
                }

                fitText = fit
                    ? $"，判定：可放下（平面 {planeLength:F2}m x {planeWidth:F2}m）"
                    : $"，判定：放不下（平面 {planeLength:F2}m x {planeWidth:F2}m）";
            }
        }

        return $"模型占地：长 {lengthMeters:F2}m，宽 {widthMeters:F2}m{fitText}";
    }

    public bool CanPlaceCurrentSelection(out string reason)
    {
        reason = "";

        if (!hasLastMeasuredFootprint)
        {
            // No measured footprint yet; keep old behavior and allow placement.
            return true;
        }

        if (planeEstimator == null)
        {
            return true;
        }

        float planeLength = planeEstimator.CurrentPlaneLength;
        float planeWidth = planeEstimator.CurrentPlaneWidth;
        if (planeLength <= 0f || planeWidth <= 0f)
        {
            return true;
        }

        bool fit;
        if (strictLengthWidthFit)
        {
            fit = lastMeasuredLengthMeters <= planeLength && lastMeasuredWidthMeters <= planeWidth;
        }
        else
        {
            fit = (lastMeasuredLengthMeters <= planeLength && lastMeasuredWidthMeters <= planeWidth) ||
                  (lastMeasuredLengthMeters <= planeWidth && lastMeasuredWidthMeters <= planeLength);
        }

        if (!fit)
        {
            reason = "空间不足";
        }
        return fit;
    }

    public void ShowPlacementBlockedHint(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "空间不足" : message.Trim();
        if (placementBlockedPopupRoot != null)
        {
            if (placementBlockedPopupText != null)
            {
                placementBlockedPopupText.text = text;
            }

            if (placementBlockedPopupRoutine != null)
            {
                StopCoroutine(placementBlockedPopupRoutine);
            }
            placementBlockedPopupRoutine = StartCoroutine(ShowPlacementBlockedPopupRoutine());
        }
        else if (currentAnimalTextShort != null)
        {
            currentAnimalTextShort.text = text;
        }
        Debug.LogWarning("[AnimalSelectionPanelController] " + text);
    }

    private IEnumerator ShowPlacementBlockedPopupRoutine()
    {
        placementBlockedPopupRoot.SetActive(true);
        if (placementBlockedPopupCanvasGroup != null)
        {
            placementBlockedPopupCanvasGroup.alpha = 1f;
        }

        float duration = Mathf.Max(0.2f, placementBlockedPopupDuration);
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < duration)
        {
            float t = (Time.unscaledTime - start) / duration;
            if (placementBlockedPopupCanvasGroup != null)
            {
                placementBlockedPopupCanvasGroup.alpha = 1f - t;
            }
            yield return null;
        }

        if (placementBlockedPopupCanvasGroup != null)
        {
            placementBlockedPopupCanvasGroup.alpha = 0f;
        }
        placementBlockedPopupRoot.SetActive(false);
        placementBlockedPopupRoutine = null;
    }
}
