using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class AnimalIntroTTSController : MonoBehaviour
{
    [Serializable]
    public class AnimalSfxConfig
    {
        public string animalId;
        public AudioClip clip;
    }

    [Serializable]
    private class IntroRequest
    {
        public string animalId;
        public string variant;
    }

    [Serializable]
    private class IntroResponse
    {
        public string text;
    }

    [Header("Refs")]
    public TileAnimalFlowController flowController;
    public TileTapDetector tapDetector;
    public AndroidNativeTTS androidNativeTTS;

    [Header("Intro Dialog")]
    public GameObject introDialogRoot;
    public TMP_Text introDialogText;
    public GameObject reopenIntroButton;
    public bool autoShowIntroDialog = true;

    [Header("API")]
    public bool enableAiIntro = true;
    public string introApiUrl = "http://115.190.8.234/ARAnimal-api/api/ai/animal-intro";
    [Range(3, 60)] public int requestTimeoutSeconds = 60;
    [Range(3, 120)] public int hardTimeoutSeconds = 75;
    [Range(1, 10)] public int heartbeatLogSeconds = 3;
    public bool autoPlayTTS = false;

    [Header("Fallback")]
    public bool useLocalFallbackWhenApiFailed = true;
    [TextArea(2, 5)] public string localFallbackTemplate = "{animalId} 是一种生活在森林中的动物。";

    [Header("Debug")]
    public bool debugLog = true;

    [Header("Dedup")]
    [Range(0f, 3f)] public float duplicateSuppressSeconds = 1.2f;

    [Header("Animal SFX")]
    public bool playAnimalSfxOnTap = true;
    public AudioSource animalSfxSource;
    [Range(0f, 3f)] public float animalSfxCooldownSeconds = 1.0f;
    [Range(0f, 1f)] public float animalSfxVolume = 1.0f;
    public List<AnimalSfxConfig> animalSfxConfigs = new List<AnimalSfxConfig>();

    private Coroutine currentRoutine;
    private int requestSerial;
    private float lastRequestTime = -999f;
    private string lastRequestKey = "";
    private string lastIntroContent = "";
    private bool hasIntroContent;
    private float lastSfxTime = -999f;

    private void Awake()
    {
        if (flowController == null) flowController = FindObjectOfType<TileAnimalFlowController>();
        if (tapDetector == null) tapDetector = FindObjectOfType<TileTapDetector>();
        if (androidNativeTTS == null) androidNativeTTS = FindObjectOfType<AndroidNativeTTS>();
    }

    private void Start()
    {
        Log($"startup: scene={SceneManager.GetActiveScene().name}, introApiUrl={introApiUrl}, requestTimeoutSeconds={requestTimeoutSeconds}, hardTimeoutSeconds={hardTimeoutSeconds}");
        if (introDialogRoot != null) introDialogRoot.SetActive(false);
        if (reopenIntroButton != null) reopenIntroButton.SetActive(false);
    }

    private void OnEnable()
    {
        if (tapDetector != null) tapDetector.OnAnimalTapped += OnAnimalTapped;
    }

    private void OnDisable()
    {
        if (tapDetector != null) tapDetector.OnAnimalTapped -= OnAnimalTapped;
    }

    private void OnAnimalTapped(GameObject animalObject, Vector3 worldPos)
    {
        if (animalObject == null) return;

        string animalId = GuessAnimalIdByName(animalObject.name);
        string variant = animalObject.name;
        if (string.IsNullOrWhiteSpace(animalId))
        {
            Log($"animal tapped but id unknown: {animalObject.name}");
            return;
        }

        TryPlayAnimalSfx(animalId);
        RequestIntro(animalId, variant);
    }

    public void RequestIntro(string animalId, string variant)
    {
        string id = string.IsNullOrWhiteSpace(animalId) ? "" : animalId.Trim().ToLowerInvariant();
        string v = string.IsNullOrWhiteSpace(variant) ? "" : NormalizeVariantForDedup(variant);
        string key = id + "|" + v;
        float now = Time.realtimeSinceStartup;
        if (key == lastRequestKey && (now - lastRequestTime) <= duplicateSuppressSeconds)
        {
            Log($"request intro ignored (dedup): key={key}, delta={(now - lastRequestTime):F2}s");
            return;
        }

        lastRequestKey = key;
        lastRequestTime = now;

        ShowLoadingDialog();

        if (currentRoutine != null)
        {
            Log("stop previous intro routine");
            StopCoroutine(currentRoutine);
            currentRoutine = null;
        }

        requestSerial++;
        int serial = requestSerial;
        Log($"request intro #{serial}: id={animalId}, variant={variant}");
        currentRoutine = StartCoroutine(RequestIntroRoutine(animalId, variant, serial));
    }

    private IEnumerator RequestIntroRoutine(string animalId, string variant, int serial)
    {
        if (string.IsNullOrWhiteSpace(introApiUrl))
        {
            Log($"intro routine #{serial}: introApiUrl is empty, use fallback.");
            ApplyIntroText(BuildFallbackText(animalId), false);
            currentRoutine = null;
            yield break;
        }

        IntroRequest payload = new IntroRequest
        {
            animalId = string.IsNullOrWhiteSpace(animalId) ? "" : animalId.Trim(),
            variant = string.IsNullOrWhiteSpace(variant) ? "" : variant.Trim()
        };

        string requestJson = JsonUtility.ToJson(payload);

        using (UnityWebRequest request = new UnityWebRequest(introApiUrl.Trim(), UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = Encoding.UTF8.GetBytes(requestJson);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeoutSeconds;

            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            float startTime = Time.realtimeSinceStartup;
            float nextHeartbeatTime = startTime + Mathf.Max(1, heartbeatLogSeconds);

            while (!op.isDone)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= hardTimeoutSeconds)
                {
                    request.Abort();
                    Log($"intro routine #{serial}: hard timeout reached ({elapsed:F1}s), request aborted.");
                    break;
                }

                if (Time.realtimeSinceStartup >= nextHeartbeatTime)
                {
                    Log($"intro routine #{serial}: waiting... elapsed={elapsed:F1}s, upload={request.uploadProgress:P0}, download={request.downloadProgress:P0}");
                    nextHeartbeatTime += Mathf.Max(1, heartbeatLogSeconds);
                }

                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log($"intro request failed #{serial}: {request.error}, code={request.responseCode}");
                HandleIntroFail(animalId);
                currentRoutine = null;
                yield break;
            }

            string json = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (string.IsNullOrWhiteSpace(json))
            {
                Log($"intro response empty #{serial}");
                HandleIntroFail(animalId);
                currentRoutine = null;
                yield break;
            }

            IntroResponse response;
            try
            {
                response = JsonUtility.FromJson<IntroResponse>(json);
            }
            catch (Exception exception)
            {
                Log($"intro parse failed #{serial}: {exception.Message}");
                HandleIntroFail(animalId);
                currentRoutine = null;
                yield break;
            }

            if (response == null || string.IsNullOrWhiteSpace(response.text))
            {
                Log($"intro response invalid #{serial}");
                HandleIntroFail(animalId);
                currentRoutine = null;
                yield break;
            }

            string intro = response.text.Trim();
            ApplyIntroText(intro, true);

            if (autoPlayTTS)
            {
                if (androidNativeTTS != null)
                {
                    androidNativeTTS.Speak(intro);
                }
                else
                {
                    Log("autoPlayTTS=true but androidNativeTTS is null.");
                }
            }

            currentRoutine = null;
        }
    }

    private void HandleIntroFail(string animalId)
    {
        if (!useLocalFallbackWhenApiFailed) return;
        ApplyIntroText(BuildFallbackText(animalId), false);
    }

    private string BuildFallbackText(string animalId)
    {
        string safeAnimalId = string.IsNullOrWhiteSpace(animalId) ? "动物" : animalId.Trim();
        string template = string.IsNullOrWhiteSpace(localFallbackTemplate)
            ? "{animalId} 是一种生活在森林中的动物。"
            : localFallbackTemplate;
        return template.Replace("{animalId}", safeAnimalId);
    }

    private void ApplyIntroText(string text, bool showDialog)
    {
        lastIntroContent = text ?? "";
        hasIntroContent = !string.IsNullOrWhiteSpace(lastIntroContent);

        if (introDialogText != null)
        {
            introDialogText.text = text ?? "";
            Log($"introDialogText applied: target={introDialogText.name}, len={text?.Length ?? 0}");
        }
        else
        {
            Log("introDialogText is NULL, cannot apply text.");
        }

        if (showDialog && autoShowIntroDialog)
        {
            SetIntroDialogVisible(true);
        }
    }

    private void ShowLoadingDialog()
    {
        hasIntroContent = false;
        if (introDialogText != null)
        {
            introDialogText.text = "介绍生成中...";
        }

        SetIntroDialogVisible(true);
    }

    public void OnClickReadIntro()
    {
        if (string.IsNullOrWhiteSpace(lastIntroContent))
        {
            Log("OnClickReadIntro ignored: no intro content.");
            return;
        }

        if (androidNativeTTS != null)
        {
            androidNativeTTS.Speak(lastIntroContent);
        }
        else
        {
            Log("ReadIntro clicked but androidNativeTTS is null.");
        }
    }

    public void OnClickStopIntro()
    {
        if (androidNativeTTS != null)
        {
            androidNativeTTS.StopSpeak();
            Log("StopIntro clicked: stop speak requested.");
        }
        else
        {
            Log("StopIntro clicked but androidNativeTTS is null.");
        }
    }

    public void OnClickCloseIntro()
    {
        SetIntroDialogVisible(false);
    }

    public void OnClickReopenIntro()
    {
        if (!hasIntroContent)
        {
            Log("ReopenIntro ignored: no intro content.");
            return;
        }

        SetIntroDialogVisible(true);
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log("[AnimalIntroTTSController] " + message);
        }
    }

    private void SetIntroDialogVisible(bool visible)
    {
        if (introDialogRoot != null)
        {
            introDialogRoot.SetActive(visible);
        }

        if (reopenIntroButton != null)
        {
            bool showReopen = !visible && hasIntroContent;
            reopenIntroButton.SetActive(showReopen);
        }
    }

    private static string GuessAnimalIdByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return "";

        string lower = objectName.ToLowerInvariant();
        if (lower.Contains("bear")) return "bear";
        if (lower.Contains("boar")) return "boar";
        if (lower.Contains("deer")) return "deer";
        if (lower.Contains("fox")) return "fox";
        if (lower.Contains("hare") || lower.Contains("rabbit")) return "hare";
        if (lower.Contains("moose")) return "moose";
        if (lower.Contains("wolf")) return "wolf";
        return "";
    }

    private static string NormalizeVariantForDedup(string variant)
    {
        if (string.IsNullOrWhiteSpace(variant)) return "";

        string v = variant.Trim();
        const string cloneSuffix = "(Clone)";
        int i = v.IndexOf(cloneSuffix, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            v = v.Substring(0, i).Trim();
        }
        return v.ToLowerInvariant();
    }

    private void TryPlayAnimalSfx(string animalId)
    {
        if (!playAnimalSfxOnTap) return;
        if (string.IsNullOrWhiteSpace(animalId)) return;

        float now = Time.realtimeSinceStartup;
        if (now - lastSfxTime <= animalSfxCooldownSeconds) return;

        if (animalSfxSource == null)
        {
            Log("animal sfx skipped: animalSfxSource is null.");
            return;
        }

        AudioClip clip = FindAnimalSfxClip(animalId);
        if (clip == null)
        {
            Log($"animal sfx skipped: no clip configured for animalId={animalId}.");
            return;
        }

        animalSfxSource.PlayOneShot(clip, animalSfxVolume);
        lastSfxTime = now;
    }

    private AudioClip FindAnimalSfxClip(string animalId)
    {
        if (animalSfxConfigs == null || animalSfxConfigs.Count == 0) return null;

        string id = animalId.Trim().ToLowerInvariant();
        for (int i = 0; i < animalSfxConfigs.Count; i++)
        {
            AnimalSfxConfig config = animalSfxConfigs[i];
            if (config == null || string.IsNullOrWhiteSpace(config.animalId) || config.clip == null)
            {
                continue;
            }

            if (config.animalId.Trim().ToLowerInvariant() == id)
            {
                return config.clip;
            }
        }

        return null;
    }
}
