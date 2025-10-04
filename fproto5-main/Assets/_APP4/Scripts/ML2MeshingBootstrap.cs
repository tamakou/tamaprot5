using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MagicLeap.Android; // Permissions
using MagicLeap.OpenXR.Features.Meshing; // MagicLeapMeshingFeature

/// <summary>
/// ML2 OpenXR メッシング／プレーン検出の起動管理＋停止/再開APIを提供。
/// - 起動時にメッシュ＆平面を開始
/// - 任意タイミングで StopMapping()/ResumeMapping() 可能
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class ML2MeshingBootstrap : MonoBehaviour
{
  [Header("References")]
  [SerializeField] private ARMeshManager meshManager;     // 環境メッシュ
  [SerializeField] private ARPlaneManager planeManager;   // 平面（床抽出用）
  [SerializeField] private Transform meshingVolume;       // メッシュ境界の中心/回転/スケールを表す空のGO

  [Header("Meshing Bounds (meters)")]
  [Tooltip("XZの幅（m）")][SerializeField] private float boundsSizeXZ = 8f;
  [Tooltip("高さ（m）")][SerializeField] private float boundsHeight = 4f;
  [Range(0.05f, 1f)]
  [Tooltip("メッシュ密度（0〜1）")][SerializeField] private float meshDensity = 0.30f;
  [Tooltip("ユーザーの頭に境界を追従させる")][SerializeField] private bool followHead = true;

  [Header("Stop/Resume options")]
  [Tooltip("StopMapping時に ARPlaneManager を無効化する")] public bool disablePlaneManagerOnStop = true;
  [Tooltip("StopMapping時に既存のプレーンを非表示にする")] public bool hideAllPlanesOnStop = true;
  [Tooltip("StopMapping時に ARMeshManager を無効化する")] public bool disableMeshManagerOnStop = true;
  [Tooltip("StopMapping時に既存の環境メッシュを破棄する")] public bool destroyAllMeshesOnStop = true;

  private MagicLeapMeshingFeature meshingFeature;

  private void Reset()
  {
    meshManager = Object.FindFirstObjectByType<ARMeshManager>();
    planeManager = Object.FindFirstObjectByType<ARPlaneManager>();
  }

  private IEnumerator Start()
  {
    if (!meshingVolume)
    {
      var go = new GameObject("MeshingVolume");
      go.transform.SetParent(transform, false);
      meshingVolume = go.transform;
    }

    if (meshManager) meshManager.enabled = false;
    if (planeManager) planeManager.enabled = false;

    // XR Mesh Subsystem 起動待ち
    yield return WaitForXRMeshSubsystem();

    // OpenXR の ML Meshing Feature を取得
    meshingFeature = OpenXRSettings.Instance?.GetFeature<MagicLeapMeshingFeature>();
    if (meshingFeature == null || !meshingFeature.enabled)
    {
      Debug.LogError("[MeshingBootstrap] MagicLeapMeshingFeature が OpenXR で有効になっていません。");
      yield break;
    }

    // SPATIAL_MAPPING 権限要求（Manifest で有効化済み前提）
    Permissions.RequestPermission(Permissions.SpatialMapping,
        _ => OnPermissionGranted(),
        p => Debug.LogError("[MeshingBootstrap] Permission denied: " + p),
        p => Debug.LogError("[MeshingBootstrap] Permission denied (don't ask again): " + p));
  }

  private IEnumerator WaitForXRMeshSubsystem()
  {
    var list = new List<XRMeshSubsystem>();
    do
    {
      SubsystemManager.GetSubsystems(list);
      yield return null;
    }
    while (list.Count == 0 || !list[0].running);
  }

  private void OnPermissionGranted()
  {
    SetupAndStartMeshing();
    SetupAndStartPlanes();
  }

  private void SetupAndStartMeshing()
  {
    // ヘッド位置を中心に境界を配置
    var cam = Camera.main ? Camera.main.transform : null;
    if (cam) meshingVolume.position = cam.position;
    meshingVolume.rotation = Quaternion.identity;
    meshingVolume.localScale = new Vector3(boundsSizeXZ, boundsHeight, boundsSizeXZ);

    if (meshManager)
    {
      meshManager.density = meshDensity; // 一部プラットフォームでは非対応だがML2はOK
      meshManager.enabled = true;
    }

    // ML2 Meshing Feature のプロパティで境界＆密度を反映
    meshingFeature.MeshRenderMode = MeshingMode.Triangles;
    meshingFeature.MeshBoundsOrigin = meshingVolume.position;
    meshingFeature.MeshBoundsRotation = meshingVolume.rotation;
    meshingFeature.MeshBoundsScale = meshingVolume.localScale;
    meshingFeature.MeshDensity = meshDensity;

    // クエリ設定の反映→Invalidate
    var q = MeshingQuerySettings.DefaultSettings();
    meshingFeature.UpdateMeshQuerySettings(in q);
    meshingFeature.InvalidateMeshes();
  }

  private void SetupAndStartPlanes()
  {
    if (!planeManager) return;
    planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal; // 床のみ
    planeManager.enabled = true;
  }

  private void LateUpdate()
  {
    if (!followHead || meshingFeature == null) return;
    var cam = Camera.main ? Camera.main.transform : null;
    if (!cam) return;

    meshingVolume.position = cam.position;
    meshingFeature.MeshBoundsOrigin = meshingVolume.position;
    // Rotation/Scale は起動時から不変なら再設定不要（必要に応じて更新）
  }

  /// <summary>環境メッシュ/平面の更新を停止し、生成済みを破棄/非表示にする</summary>
  public void StopMapping(bool? destroyMeshes = null, bool? hidePlanes = null)
  {
    // 平面側
    if (planeManager)
    {
      if (hidePlanes ?? hideAllPlanesOnStop)
      {
        // 既存トラッカブルを一括非表示
        planeManager.SetTrackablesActive(false);
      }
      if (disablePlaneManagerOnStop)
        planeManager.enabled = false;
    }

    // メッシング側
    if (meshManager)
    {
      if (disableMeshManagerOnStop)
        meshManager.enabled = false;

      if (destroyMeshes ?? destroyAllMeshesOnStop)
        meshManager.DestroyAllMeshes(); // 既存メッシュ破棄
    }
  }

  /// <summary>停止後にメッシング/平面検出を再開する</summary>
  public void ResumeMapping()
  {
    if (meshManager) meshManager.enabled = true;
    if (planeManager) planeManager.enabled = true;
    meshingFeature?.InvalidateMeshes(); // クエリ再発行
  }
}
