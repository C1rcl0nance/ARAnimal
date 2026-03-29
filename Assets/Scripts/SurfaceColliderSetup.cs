using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂载到 EasyAR SurfaceTracker 生成的 Target 物体上
/// 自动添加一个大的平面 Collider 用于射线检测
/// </summary>

public class SurfaceColliderSetup : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        // 添加一个大的 BoxCollider 模拟地面
        BoxCollider col = gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(100f, 0.01f, 100f);  // 大平面
        col.center = Vector3.zero;
    }
}