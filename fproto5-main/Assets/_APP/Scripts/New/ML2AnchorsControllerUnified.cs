// Assets/_APP/Scripts/ML2AnchorsControllerUnified.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.NativeTypes;

using MagicLeap.OpenXR.Features.LocalizationMaps;
using MagicLeap.OpenXR.Features.SpatialAnchors;
using MagicLeap.OpenXR.Subsystems;

public class ML2AnchorsControllerUnified : MonoBehaviour
{
    [Header("Scene refs")]
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] Camera mainCamera;
    [SerializeField] Transform rightController;               // �C�Ӂi���ݒ�Ȃ�J������j
    [SerializeField] GameObject anchorVisualPrefab;           // ��Cube���i�����ځj

    [Header("UI refs")]
    [SerializeField] Dropdown mapsDropdown;
    [SerializeField] Button localizeButton, spawnButton, publishButton, queryButton, deleteNearestButton, deleteAllButton;
    [SerializeField] Text statusLabel, localizationLabel;

    [Header("Options")]
    [SerializeField, Tooltip("�����ʒu�̑O���I�t�Z�b�g(m)")] float forwardMeters = 0.30f;
    [SerializeField, Tooltip("�����N�G�����a(m)")] float restoreRadiusMeters = 10f;
    [SerializeField, Tooltip("�l�b�g��M���Ɏ�����Publish����")] bool autoPublishOnNetworkReceive = true;
    [SerializeField, Tooltip("�d�������̔��苗��(m)")] float duplicateEpsilonMeters = 0.05f;

    // ML OpenXR
    MagicLeapLocalizationMapFeature mapFeature;
    MagicLeapSpatialAnchorsStorageFeature storageFeature;
    MLXrAnchorSubsystem mlAnchorSubsystem;

    // ���
    LocalizationMap[] maps = Array.Empty<LocalizationMap>();
    readonly List<ARAnchor> localAnchors = new();       // ���ۑ�
    readonly HashSet<TrackableId> storedIds = new();    // �ۑ��ς�
    readonly HashSet<string> savedMapPositionIds = new(); // PlayerPrefs �Ɠ���

    const string SavedIdListKey = "ML2_SavedAnchorMapPositionIds";
    [Serializable] class IdList { public List<string> ids = new(); }
    public static ML2AnchorsControllerUnified Instance { get; private set; }

    void Awake()
    {
        Instance = this;                   // �� �ǉ�
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        if (!mainCamera) mainCamera = Camera.main;
    }

    async void Start()
    {
        await WaitSubsystems();

        mapFeature = OpenXRSettings.Instance.GetFeature<MagicLeapLocalizationMapFeature>();
        storageFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();

        if (mapFeature == null || storageFeature == null)
        {
            Log("OpenXR Features (LocalizationMap / SpatialAnchorsStorage) �������ł��B");
            enabled = false; return;
        }

        var loader = XRGeneralSettings.Instance.Manager.activeLoader;
        mlAnchorSubsystem = loader?.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;

        // ARF6: trackablesChanged
        anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);

        // Storage callbacks
        storageFeature.OnPublishComplete += OnPublishComplete;
        storageFeature.OnQueryComplete += OnQueryComplete;
        storageFeature.OnCreationCompleteFromStorage += OnCreationFromStorage;
        storageFeature.OnDeletedComplete += OnDeleteComplete;

        // Localization ��ԃC�x���g��L����
        mapFeature.EnableLocalizationEvents(true);

        // UI �z��
        WireUi();

        // �}�b�v�ꗗ
        InitMaps();

        // �ۑ�ID����
        LoadSavedIds();

        UpdateLocalizationUi();
        if (publishButton) publishButton.interactable = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (anchorManager) anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        if (storageFeature != null)
        {
            storageFeature.OnPublishComplete -= OnPublishComplete;
            storageFeature.OnQueryComplete -= OnQueryComplete;
            storageFeature.OnCreationCompleteFromStorage -= OnCreationFromStorage;
            storageFeature.OnDeletedComplete -= OnDeleteComplete;
        }
        if (mapFeature != null) mapFeature.EnableLocalizationEvents(false);
    }

    // ===== ��������V�K: Grab Release ���̍����ւ� =====
    public async Task ReanchorThisHandleAsync(AnchorHandle handle)
    {
        if (handle == null || anchorManager == null) return;

        // ���A���J�[�ƕۑ�ID��c��
        var old = handle.CurrentAnchor;
        string oldMapPosId = (mlAnchorSubsystem != null && old != null)
            ? mlAnchorSubsystem.GetAnchorMapPositionId(old) : null;

        // �V�����A���J�[�����݈ʒu�Ő���
        var pose = new Pose(handle.transform.position, handle.transform.rotation);
        var newAnchor = await SpawnLocalAnchorAtPoseAsync(pose);
        if (newAnchor == null) return;

        // �\��/�n���h����V�A���J�[�̎q�ɕt���ւ��i���[���h���W�ێ��j
        handle.transform.SetParent(newAnchor.transform, true);
        handle.Bind(newAnchor);

        // ���A���J�[��j���i�ۑ��ς݂Ȃ�X�g���[�W������폜�j
        if (!string.IsNullOrEmpty(oldMapPosId))
            storageFeature?.DeleteStoredSpatialAnchors(new List<string> { oldMapPosId });

        if (old) Destroy(old.gameObject);

        // �Ȍ�APublish �{�^���� newAnchor ���ۑ������iautoPublishOnNetworkReceive ���g���Ȃ炱���� Publish ���Ă��悢�j
    }

    async Task WaitSubsystems()
    {
        while (XRGeneralSettings.Instance == null ||
               XRGeneralSettings.Instance.Manager == null ||
               XRGeneralSettings.Instance.Manager.activeLoader == null)
        {
            await Task.Yield();
        }
    }

    void WireUi()
    {
        if (localizeButton) { localizeButton.onClick.RemoveAllListeners(); localizeButton.onClick.AddListener(LocalizeSelectedMap); }
        if (spawnButton) { spawnButton.onClick.RemoveAllListeners(); spawnButton.onClick.AddListener(async () => await SpawnLocalAnchorAsync()); }
        if (publishButton) { publishButton.onClick.RemoveAllListeners(); publishButton.onClick.AddListener(PublishTrackingLocals); }
        if (queryButton) { queryButton.onClick.RemoveAllListeners(); queryButton.onClick.AddListener(QueryNearby); }
        if (deleteNearestButton) { deleteNearestButton.onClick.RemoveAllListeners(); deleteNearestButton.onClick.AddListener(DeleteNearestStored); }
        if (deleteAllButton) { deleteAllButton.onClick.RemoveAllListeners(); deleteAllButton.onClick.AddListener(DeleteAllStored); }
    }

    void InitMaps()
    {
        if (mapFeature.GetLocalizationMapsList(out maps) == XrResult.Success && mapsDropdown)
        {
            mapsDropdown.ClearOptions();
            mapsDropdown.AddOptions(maps.Select(m => m.Name).ToList());
        }
    }

    // ---------- Buttons ----------
    void LocalizeSelectedMap()
    {
        if (maps.Length == 0) { Log("���[�J���C�Y�Ώۂ� Map ������܂���"); return; }
        int idx = Mathf.Clamp(mapsDropdown ? mapsDropdown.value : 0, 0, maps.Length - 1);
        string uuid = maps[idx].MapUUID;

        var res = mapFeature.RequestMapLocalization(uuid);
        if (res != XrResult.Success) { Log("Localize ���s: " + res); return; }

        // ������|��
        foreach (var a in FindObjectsByType<ARAnchor>(FindObjectsSortMode.None)) Destroy(a.gameObject);
        localAnchors.Clear(); storedIds.Clear();

        Log("���[�J���C�Y�v��: " + maps[idx].Name);
        UpdateLocalizationUi();
    }

    public async Task SpawnLocalAnchorAsync()
    {
        var src = rightController ? rightController : (mainCamera ? mainCamera.transform : null);
        if (!src) { Log("������ Transform ���ݒ�"); return; }

        var pose = new Pose(
            src.position + src.forward * forwardMeters,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(src.forward, Vector3.up), Vector3.up)
        );

        await SpawnLocalAnchorAtPoseAsync(pose);
        Log("���[�J���A���J�[�����i���ۑ��j");
    }

    async Task<ARAnchor> SpawnLocalAnchorAtPoseAsync(Pose pose)
    {
        var result = await anchorManager.TryAddAnchorAsync(pose);
        if (!result.status.IsSuccess())
        {
            Log("Anchor �������s: " + (XrResult)result.status.nativeStatusCode);
            return null;
        }

        var anchor = result.value;
        if (anchor != null)
        {
            localAnchors.Add(anchor);
            if (anchorVisualPrefab && anchor.GetComponentInChildren<MeshRenderer>() == null)
            {
                var vis = Instantiate(anchorVisualPrefab, anchor.transform);
                TintAll(vis, Color.grey);
            }
        }
        return anchor;
    }

    void PublishTrackingLocals()
    {
        mapFeature.GetLatestLocalizationMapData(out LocalizationEventData d);
        if (d.State != LocalizationMapState.Localized) { Log("Publish �ɂ� Localized ���K�v"); return; }

        var targets = localAnchors.Where(a => a && a.trackingState == TrackingState.Tracking && !a.pending).ToList();
        if (targets.Count == 0) { Log("Publish �ΏۂȂ�"); return; }

        // 0 = �������B������ OnPublishComplete�B
        storageFeature.PublishSpatialAnchorsToStorage(targets, 0);
        Log($"Publish �v��: {targets.Count} ��");
    }

    void QueryNearby()
    {
        var origin = rightController ? rightController.position : (mainCamera ? mainCamera.transform.position : Vector3.zero);
        if (!storageFeature.QueryStoredSpatialAnchors(origin, Mathf.Max(restoreRadiusMeters, 0.1f)))
            Log("Query ���s");
        else
            Log($"Query �v���i{restoreRadiusMeters}m�ȓ��j");
    }

    void DeleteNearestStored()
    {
        var a = FindClosestStoredAnchorNear(GetRefPosition(), 100f);
        if (!a) { Log("�ߖT�̕ۑ��ς݃A���J�[�Ȃ�"); return; }
        string id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
        if (string.IsNullOrEmpty(id)) { Log("MapPositionId �擾���s"); return; }
        storageFeature.DeleteStoredSpatialAnchors(new List<string> { id });
        Log("�폜�v��: " + id);
    }

    void DeleteAllStored()
    {
        var ids = new List<string>();
        foreach (var a in anchorManager.trackables)
        {
            var id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        if (ids.Count == 0) { Log("�폜�ΏۂȂ�"); return; }
        storageFeature.DeleteStoredSpatialAnchors(ids);
        Log($"�ꊇ�폜�v��: {ids.Count} ��");
    }

    // ---------- Storage callbacks ----------
    void OnPublishComplete(ulong anchorId, string mapPosId)
    {
        // �ۑ��ς݂֏��i�i�����ڂ𔒁j
        var anchor = FindAnchorByMapPositionId(mapPosId);
        if (!anchor && mlAnchorSubsystem != null)
        {
            var pose = mlAnchorSubsystem.GetAnchorPoseFromId(anchorId); // �� GetAnchorPoseFromId�i�񐄏�API�͕s�g�p�j
            anchor = FindClosestLocalAnchor(pose.position, duplicateEpsilonMeters);
        }
        if (anchor)
        {
            storedIds.Add(anchor.trackableId);
            localAnchors.Remove(anchor);
            savedMapPositionIds.Add(mapPosId);
            SaveSavedIds();
            TintAll(anchor.gameObject, Color.white);

            // �� Fusion 2 �� Pose ���u���[�h�L���X�g�iAR Cloud �Ȃ��̋��L�j
            mapFeature.GetLatestLocalizationMapData(out var d);
            string uuid = (d.State == LocalizationMapState.Localized && d.Map.Name != null) ? d.Map.MapUUID : "";
            var pose = anchor.transform.GetPose(); // �g�����\�b�h��������Ύ��O�� position/rotation ���g��
            AnchorNetBridge.Instance?.BroadcastAnchorPose(pose.position, pose.rotation, uuid);

            Log($"Publish ����: {mapPosId}");
        }
        else
        {
            Log($"Publish �����iAnchor������j: {mapPosId}");
        }
    }

    void OnQueryComplete(List<string> ids)
    {
        if (ids == null || ids.Count == 0) { Log("Query: �����炸"); return; }

        // ���ɃV�[���ɂ���ID�����O
        var existing = new HashSet<string>();
        foreach (var a in anchorManager.trackables)
        {
            var id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(id)) existing.Add(id);
        }
        var toCreate = ids.Where(id => !existing.Contains(id)).ToList();
        if (toCreate.Count == 0) { Log("�����ΏۂȂ�"); return; }

        if (!storageFeature.CreateSpatialAnchorsFromStorage(toCreate))
            Log("CreateSpatialAnchorsFromStorage ���s");
        else
            Log($"�����v��: {toCreate.Count} ��");
    }

    void OnCreationFromStorage(Pose pose, ulong anchorId, string mapPosId, XrResult result)
    {
        if (result != XrResult.Success) { Log($"�������s: {result}"); return; }
        savedMapPositionIds.Add(mapPosId);
        SaveSavedIds();
        Log($"�����ʒm: {mapPosId}");
        // ���̂� trackablesChanged(added) �ŗ���
    }

    void OnDeleteComplete(List<string> deletedIds)
    {
        foreach (var id in deletedIds)
        {
            var a = FindAnchorByMapPositionId(id);
            if (a) Destroy(a.gameObject);
            savedMapPositionIds.Remove(id);
        }
        SaveSavedIds();
        Log($"�폜����: {deletedIds.Count} ��");
    }

    // ---------- ARF6: trackablesChanged ----------
    void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> changes)
    {
        foreach (var a in changes.added)
        {
            var id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            bool stored = !string.IsNullOrEmpty(id);
            EnsureVisual(a, stored);
            if (stored) { storedIds.Add(a.trackableId); TintAll(a.gameObject, Color.white); }
            else { if (!localAnchors.Contains(a)) localAnchors.Add(a); TintAll(a.gameObject, Color.grey); }
        }
        foreach (var a in changes.updated)
        {
            var id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(id) && !storedIds.Contains(a.trackableId))
            {
                storedIds.Add(a.trackableId);
                localAnchors.Remove(a);
                EnsureVisual(a, true);
                TintAll(a.gameObject, Color.white);
            }
        }
        foreach (var kv in changes.removed) // KeyValuePair<TrackableId, ARAnchor>
        {
            localAnchors.Remove(kv.Value);
            storedIds.Remove(kv.Key);
        }
        UpdateLocalizationUi();
    }

    // ---------- �l�b�g��M�iFusion 2 �o�R�j ----------
    public async void OnNetworkAnchorPublished(Vector3 pos, Quaternion rot, string mapUUID)
    {
        // ���� Space�iMapUUID�j�Ƀ��[�J���C�Y���Ă��邩�m�F
        mapFeature.GetLatestLocalizationMapData(out var d);
        if (d.State != LocalizationMapState.Localized || d.Map.MapUUID != mapUUID)
        {
            Log($"�� Space �̃A���J�[�ʒm����M�i�����j: {mapUUID}");
            return;
        }

        // �d���`�F�b�N
        var existStored = FindClosestStoredAnchorNear(pos, duplicateEpsilonMeters);
        var existLocal = FindClosestLocalAnchor(pos, duplicateEpsilonMeters);
        if (existStored || existLocal) return;

        var a = await SpawnLocalAnchorAtPoseAsync(new Pose(pos, rot));
        if (a != null && autoPublishOnNetworkReceive)
        {
            PublishTrackingLocals(); // Tracking ���̃��[�J���� Publish
        }
    }

    // ---------- helpers ----------
    void EnsureVisual(ARAnchor a, bool stored)
    {
        if (!a || !anchorVisualPrefab) return;
        if (!a.GetComponentInChildren<MeshRenderer>())
        {
            var vis = Instantiate(anchorVisualPrefab, a.transform);
            TintAll(vis, stored ? Color.white : Color.grey);
        }
    }
    void TintAll(GameObject go, Color c)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.material.color = c;
    }
    ARAnchor FindAnchorByMapPositionId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var a in anchorManager.trackables)
            if (mlAnchorSubsystem != null && mlAnchorSubsystem.GetAnchorMapPositionId(a) == id) return a;
        return null;
    }
    ARAnchor FindClosestLocalAnchor(Vector3 pos, float maxDist)
    {
        ARAnchor best = null; float bestD = maxDist;
        foreach (var a in localAnchors)
        {
            if (!a) continue;
            float d = Vector3.Distance(a.transform.position, pos);
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best;
    }
    ARAnchor FindClosestStoredAnchorNear(Vector3 pos, float maxDist)
    {
        ARAnchor best = null; float bestD = maxDist;
        foreach (var a in anchorManager.trackables)
        {
            var id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (string.IsNullOrEmpty(id)) continue;
            float d = Vector3.Distance(a.transform.position, pos);
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best;
    }
    Vector3 GetRefPosition() => rightController ? rightController.position : (mainCamera ? mainCamera.transform.position : Vector3.zero);

    void UpdateLocalizationUi()
    {
        if (mapFeature == null) return;
        mapFeature.GetLatestLocalizationMapData(out LocalizationEventData d);
        if (localizationLabel)
        {
            string info = $"State:{d.State} Conf:{d.Confidence}";
            if (d.State == LocalizationMapState.Localized && d.Map.Name != null) info += $"  Name:{d.Map.Name}";
            localizationLabel.text = info;
        }
        if (publishButton) publishButton.interactable = (d.State == LocalizationMapState.Localized);
    }

    void LoadSavedIds()
    {
        savedMapPositionIds.Clear();
        var json = PlayerPrefs.GetString(SavedIdListKey, "");
        if (string.IsNullOrEmpty(json)) return;
        try { var l = JsonUtility.FromJson<IdList>(json); foreach (var id in l.ids) savedMapPositionIds.Add(id); } catch { }
    }
    void SaveSavedIds()
    {
        var l = new IdList { ids = savedMapPositionIds.ToList() };
        PlayerPrefs.SetString(SavedIdListKey, JsonUtility.ToJson(l));
        PlayerPrefs.Save();
    }
    void Log(string msg) { if (statusLabel) statusLabel.text = msg; Debug.Log("[ML2AnchorsControllerUnified] " + msg); }
}

// Transform �g���iPose �擾�p�F������Ύg�p���Ȃ��Ă�OK�j
static class TransformExt
{
    public static Pose GetPose(this Transform t) => new Pose(t.position, t.rotation);
}