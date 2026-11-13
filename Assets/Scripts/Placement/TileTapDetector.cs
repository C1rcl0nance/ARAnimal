using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
    public bool prioritizeAnimalTap = true;
    public string animalTag = "Animal";
    public bool detectAnimalByNameFallback = true;

    [Header("Debug")]
    public bool debugLog = true;
    public bool debugDrawRay = true;
    public float rayLength = 100f;

    public Action<GameObject, Vector3> OnTileTapped;
    public Action<GameObject, Vector3> OnAnimalTapped;

    void Awake() { Debug.Log("[TapDetector] Awake"); }
    void Start() { Debug.Log("[TapDetector] Start"); }

    void Update()
    {
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
        Log($"[Tap] HIT collider={hitGo.name}, root={rootGo.name}, hitTag={hitGo.tag}, rootTag={rootGo.tag}, layer={LayerMask.LayerToName(hitGo.layer)}");

        bool animalByTag = HasTagInParents(hit.collider.transform, animalTag);
        bool animalByName = detectAnimalByNameFallback && HasAnimalNameInParents(hit.collider.transform);
        if (prioritizeAnimalTap && (animalByTag || animalByName))
        {
            GameObject animalGo = animalByTag
                ? FindTaggedParent(hit.collider.transform, animalTag)
                : FindAnimalNameParent(hit.collider.transform);
            if (animalGo == null)
            {
                animalGo = hitGo;
            }
            Log($"[Tap] ANIMAL -> OnAnimalTapped(hit={animalGo.name}, byTag={animalByTag}, byName={animalByName}, point={hit.point})");
            OnAnimalTapped?.Invoke(animalGo, hit.point);
            return;
        }

        bool pass = true;

        if (useTagCheck)
        {
            bool tagOk = HasTagInParents(hit.collider.transform, tileTag);
            Log($"[Tap] tag check: need={tileTag}, pass={tagOk}");
            pass &= tagOk;
        }

        if (useNameCheck)
        {
            bool nameOk = HasNameInParents(hit.collider.transform, tileName);
            Log($"[Tap] name check: need={tileName}, pass={nameOk}");
            pass &= nameOk;
        }

        if (!pass && IsCombinedGridOverlay(hit.collider.transform))
        {
            Log("[Tap] fallback pass: combined grid overlay hit");
            pass = true;
        }

        if (!pass)
        {
            Log("[Tap] FAIL: hit object filtered out by tag/name check");
            return;
        }

        Log($"[Tap] SUCCESS -> OnTileTapped(hit={hitGo.name}, point={hit.point})");
        OnTileTapped?.Invoke(hitGo, hit.point);
    }

    private static bool HasTagInParents(Transform start, string expectedTag)
    {
        if (start == null || string.IsNullOrWhiteSpace(expectedTag))
        {
            return false;
        }

        Transform current = start;
        while (current != null)
        {
            if (current.CompareTag(expectedTag))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool HasNameInParents(Transform start, string expectedName)
    {
        if (start == null || string.IsNullOrWhiteSpace(expectedName))
        {
            return false;
        }

        Transform current = start;
        while (current != null)
        {
            string currentName = current.name;
            if (currentName == expectedName || currentName.StartsWith(expectedName))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static GameObject FindTaggedParent(Transform start, string expectedTag)
    {
        if (start == null || string.IsNullOrWhiteSpace(expectedTag))
        {
            return null;
        }

        Transform current = start;
        while (current != null)
        {
            if (current.CompareTag(expectedTag))
            {
                return current.gameObject;
            }
            current = current.parent;
        }

        return null;
    }

    private static bool HasAnimalNameInParents(Transform start)
    {
        if (start == null)
        {
            return false;
        }

        Transform current = start;
        while (current != null)
        {
            if (IsAnimalName(current.name))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static GameObject FindAnimalNameParent(Transform start)
    {
        if (start == null)
        {
            return null;
        }

        Transform current = start;
        while (current != null)
        {
            if (IsAnimalName(current.name))
            {
                return current.gameObject;
            }
            current = current.parent;
        }
        return null;
    }

    private static bool IsAnimalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string lower = name.ToLowerInvariant();
        return lower.Contains("bear")
               || lower.Contains("boar")
               || lower.Contains("deer")
               || lower.Contains("fox")
               || lower.Contains("hare")
               || lower.Contains("rabbit")
               || lower.Contains("moose")
               || lower.Contains("wolf");
    }

    private static bool IsCombinedGridOverlay(Transform start)
    {
        if (start == null)
        {
            return false;
        }

        Transform current = start;
        while (current != null)
        {
            if (string.Equals(current.name, "CombinedGridOverlay", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void Log(string msg)
    {
        if (debugLog) Debug.Log(msg);
    }
}
