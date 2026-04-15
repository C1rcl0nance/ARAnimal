using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // 新输入系统

public class TileTapDetector : MonoBehaviour
{
    [Header("Raycast")]
    public Camera raycastCamera;
    public LayerMask layerMask = ~0;
    public bool useTagCheck = true;
    public string tileTag = "Tile";
    public bool useNameCheck = false;
    public string tileName = "TileCell";
    public bool ignoreWhenPointerOverUI = true;

    [Header("Debug")]
    public bool debugLog = true;
    public bool debugDrawRay = true;
    public float rayLength = 100f;

    public Action<GameObject, Vector3> OnTileTapped;

    void Awake() { Debug.Log("[TapDetector] Awake"); }
    void Start() { Debug.Log("[TapDetector] Start"); }

    void Update()
    {
        // 1) 触摸优先（安卓/iOS）
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 pos = Touchscreen.current.primaryTouch.position.ReadValue();
            int touchId = Touchscreen.current.primaryTouch.touchId.ReadValue();

            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touchId))
            {
                Log("[Tap] blocked by UI (Touch/NewInput)");
                return;
            }

            TryRaycast(pos, "Touch(NewInput)");
            return;
        }

        // 2) 鼠标兜底（编辑器/PC）
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 pos = Mouse.current.position.ReadValue();

            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Log("[Tap] blocked by UI (Mouse/NewInput)");
                return;
            }

            TryRaycast(pos, "Mouse(NewInput)");
            return;
        }
    }

    private void TryRaycast(Vector2 screenPos, string inputType)
    {
        var cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
        {
            Log("[Tap] FAIL: no camera (raycastCamera and Camera.main are null)");
            return;
        }

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (debugDrawRay) Debug.DrawRay(ray.origin, ray.direction * rayLength, Color.cyan, 1.2f);

        Log($"[Tap] {inputType} pos={screenPos}, cam={cam.name}, mask={layerMask.value}");

        if (!Physics.Raycast(ray, out RaycastHit hit, rayLength, layerMask))
        {
            Log("[Tap] FAIL: Physics.Raycast no hit");
            return;
        }

        var hitGo = hit.collider.gameObject;
        var rootGo = hit.collider.transform.root.gameObject;

        Log($"[Tap] HIT collider={hitGo.name}, root={rootGo.name}, tag={rootGo.tag}, layer={LayerMask.LayerToName(rootGo.layer)}");

        bool pass = true;

        if (useTagCheck)
        {
            bool tagOk = rootGo.CompareTag(tileTag);
            Log($"[Tap] tag check: need={tileTag}, actual={rootGo.tag}, pass={tagOk}");
            pass &= tagOk;
        }

        if (useNameCheck)
        {
            bool nameOk = rootGo.name == tileName || rootGo.name.StartsWith(tileName);
            Log($"[Tap] name check: need={tileName}, actual={rootGo.name}, pass={nameOk}");
            pass &= nameOk;
        }

        if (!pass)
        {
            Log("[Tap] FAIL: hit object filtered out by tag/name check");
            return;
        }

        Log($"[Tap] SUCCESS -> OnTileTapped(root={rootGo.name}, point={hit.point})");
        OnTileTapped?.Invoke(rootGo, hit.point);
    }

    private void Log(string msg)
    {
        if (debugLog) Debug.Log(msg);
    }
}