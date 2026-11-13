using UnityEngine;

public class AndroidNativeTTS : MonoBehaviour
{
    [SerializeField] private bool initOnAwake = true;
    [SerializeField] private bool debugLog = true;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass bridge;
#endif

    private void Awake()
    {
        if (initOnAwake)
        {
            Initialize();
        }
    }

    public void Initialize()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            bridge = new AndroidJavaClass("UnityTTSBridge");
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                bridge.CallStatic("init", activity);
            }
            Log("Android TTS init requested.");
        }
        catch (System.Exception e)
        {
            Log("Android TTS init failed: " + e.Message);
        }
#else
        Log("AndroidNativeTTS Initialize skipped (not Android runtime).");
#endif
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridge == null)
        {
            Initialize();
        }
        try
        {
            bridge.CallStatic("speak", text);
            Log("Android TTS speak requested. len=" + text.Trim().Length);
        }
        catch (System.Exception e)
        {
            Log("Android TTS speak failed: " + e.Message);
        }
#else
        Log("AndroidNativeTTS Speak skipped (not Android runtime). text=" + text);
#endif
    }

    public void StopSpeak()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridge == null) return;
        try { bridge.CallStatic("stop"); } catch { }
#endif
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridge != null)
        {
            try { bridge.CallStatic("shutdown"); } catch { }
            bridge = null;
        }
#endif
    }

    private void Log(string msg)
    {
        if (debugLog)
        {
            Debug.Log("[AndroidNativeTTS] " + msg);
        }
    }
}
