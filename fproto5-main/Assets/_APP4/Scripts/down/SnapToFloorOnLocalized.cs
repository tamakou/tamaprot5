using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.LocalizationMaps; // MagicLeapLocalizationMapFeature, LocalizationEventData

/// <summary>
/// ローカライゼーション完了時に、objectToDrop（原点キューブ等）を下方向に移動させ、
/// 床コライダー（平面 or 環境メッシュ）に接地させる。
/// 接地完了後はメッシング/プレーン検出を停止して負荷を削減可能。
/// </summary>
[DisallowMultipleComponent]
public class SnapToFloorOnLocalized : MonoBehaviour
{
  [Header("Target")]
  [Tooltip("床に合わせて下げる対象（Space原点に出しているキューブ等の親）")]
  public Transform objectToDrop;

  [Tooltip("任意：ここ（例: 道路標識の底面マーカー）を床に接地させたい場合に指定")]
  public Transform groundingPoint;

  [Header("Raycast")]
  [Tooltip("レイの開始高さ（対象の真上から下へ飛ばす）")]
  public float castHeight = 2.0f;
  [Tooltip("めり込み防止の隙間(m)")]
  public float padding = 0.005f;
  [Tooltip("下げるアニメ速度(m/s)")]
  public float snapSpeed = 6f;
  [Tooltip("床として判定するレイヤーマスク（未設定なら全レイヤー）")]
  public LayerMask groundMask = ~0;

  [Header("After Snap")]
  [Tooltip("接地完了後にメッシング/平面検出を停止する")]
  public bool stopMappingAfterSnap = true;
  [Tooltip("停止時に既存の環境メッシュを破棄する（軽くなる）")]
  public bool destroyMeshesOnStop = true;
  [Tooltip("停止時に既存プレーンを非表示にする")]
  public bool hidePlanesOnStop = true;

  [Tooltip("（任意）明示参照。未設定なら自動検索")]
  public ML2MeshingBootstrap bootstrap;

  [Header("Localization Event")]
  [Tooltip("MagicLeap のローカライゼーションイベントに自動で反応する")]
  public bool subscribeToMlLocalizationEvent = true;

  void Awake()
  {
    if (!bootstrap) bootstrap = FindFirstObjectByType<ML2MeshingBootstrap>();
  }

  void OnEnable()
  {
    if (subscribeToMlLocalizationEvent)
      MagicLeapLocalizationMapFeature.OnLocalizationChangedEvent += OnLocalizationChanged;
  }

  void OnDisable()
  {
    if (subscribeToMlLocalizationEvent)
      MagicLeapLocalizationMapFeature.OnLocalizationChangedEvent -= OnLocalizationChanged;
  }

  // ML2 のローカライゼーション完了で呼ばれる
  private void OnLocalizationChanged(LocalizationEventData ev)
  {
    if (ev.State == LocalizationMapState.Localized)
    {
      // SpaceTestManager が spaceOrigin を反映した直後に走らせたいので少し待つ
      StartCoroutine(SnapAfterDelay(0.25f));
    }
  }

  /// <summary>手動で実行したい時用（生成直後など）</summary>
  public void TriggerSnap() => StartCoroutine(SnapAfterDelay(0f));

  private IEnumerator SnapAfterDelay(float delay)
  {
    if (delay > 0f) yield return new WaitForSeconds(delay);
    if (objectToDrop == null) yield break;

    // 近傍に床コライダーが立ち上がるのを少し待つ（プレーン/メッシュ）
    var timeout = Time.time + 3f;
    while (Time.time < timeout)
    {
      if (HasGroundCollidersNearby(objectToDrop.position, 5f)) break;
      yield return null;
    }

    yield return StartCoroutine(SnapNow());
  }

  private bool HasGroundCollidersNearby(Vector3 center, float radius)
  {
    var hits = Physics.OverlapSphere(center, radius, groundMask, QueryTriggerInteraction.Ignore);
    foreach (var h in hits)
    {
      if (h == null) continue;
      // プレーン or 環境メッシュらしきものが近くにあればOK
      if (h.GetComponentInParent<ARPlane>() != null) return true;
      if (h is MeshCollider && h.GetComponentInParent<MeshFilter>() != null) return true;
    }
    return false;
  }

  private IEnumerator SnapNow()
  {
    // 対象の“底面”が現在のPivotからどれだけ下か（オフセット）を計算
    float bottomOffset = GetBottomOffset(objectToDrop, groundingPoint);

    // 真上から下向きへ複数ヒットを見る（自分自身のコライダーは無視）
    Vector3 origin = objectToDrop.position + Vector3.up * castHeight;
    var hits = Physics.RaycastAll(origin, Vector3.down, castHeight * 3f, groundMask, QueryTriggerInteraction.Ignore);
    if (hits == null || hits.Length == 0)
    {
      Debug.LogWarning("[SnapToFloor] Raycast miss. MeshCollider の付与を確認してください。");
      yield break;
    }

    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
    RaycastHit? groundHit = null;
    foreach (var h in hits)
    {
      if (!h.collider) continue;
      // 自分の子孫には当てない
      if (objectToDrop != null && h.collider.transform.IsChildOf(objectToDrop)) continue;
      groundHit = h; break;
    }
    if (groundHit == null) yield break;

    float targetY = groundHit.Value.point.y + bottomOffset + padding;

    // アニメ移動（縦だけ）
    float startY = objectToDrop.position.y;
    float dist = Mathf.Abs(startY - targetY);
    float duration = Mathf.Clamp(dist / snapSpeed, 0.05f, 0.6f);
    float t = 0f;
    var pos = objectToDrop.position;

    while (t < 1f)
    {
      t += Time.deltaTime / duration;
      pos.y = Mathf.Lerp(startY, targetY, Mathf.SmoothStep(0f, 1f, t));
      objectToDrop.position = pos;
      yield return null;
    }
    pos.y = targetY;
    objectToDrop.position = pos;

    // 接地完了 → マッピング停止（任意）
    if (stopMappingAfterSnap && bootstrap != null)
    {
      bootstrap.StopMapping(destroyMeshesOnStop, hidePlanesOnStop);
    }
  }

  // groundingPoint があればその点を床に接地、なければRenderer群の最下端を使う
  private static float GetBottomOffset(Transform target, Transform groundingPoint)
  {
    if (target == null) return 0f;
    if (groundingPoint != null)
      return target.position.y - groundingPoint.position.y;

    var renderers = target.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0) return 0f;

    Bounds b = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
    return target.position.y - b.min.y;
  }
}
