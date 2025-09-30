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

public class MLAnchorsUiController : MonoBehaviour
{
    [Header("Scene refs")]
    [SerializeField] private ARAnchorManager anchorManager;   // XR Origin(AR) �ɕt�^
    [SerializeField] private Camera mainCamera;       // XR �̃��C���J����
    [SerializeField] private GameObject controllerObject; // �E��R���g���[�����i�C�Ӂj
    [SerializeField] private GameObject anchorVisualPrefab; // ������Cube���i�����ځj

    [Header("UI refs (screenshot�ƑΉ�)")]
    [SerializeField] private Dropdown mapsDropdown;           // �u�}�b�v�I���v
    [SerializeField] private Button localizeButton;         // �uLocalize to Map�v
    [SerializeField] private Button spawnAnchorButton;      // �u�����v
    [SerializeField] private Button publishButton;          // �u�ۑ��v
    [SerializeField] private Button queryButton;            // �u�����v
    [SerializeField] private Button deleteNearestButton;    // �u�폜�v
    [SerializeField] private Button deleteAllButton;        // �u�S�č폜�v
    [SerializeField] private Text statusLabel;            // �C�ӁF�����̃X�e�[�^�X
    [SerializeField] private Text localizationLabel;      // �uLocalization Status:�v

    [Header("Spawn options")]
    [SerializeField, Tooltip("�������̊����O��(���[�g��)")]
    private float forwardMeters = 0.30f;

    // ML OpenXR features / subsystems
    private MagicLeapLocalizationMapFeature mapFeature;
    private MagicLeapSpatialAnchorsStorageFeature storageFeature;
    private MLXrAnchorSubsystem mlAnchorSubsystem;

    // �}�b�v�ꗗ
    private LocalizationMap[] mapList = Array.Empty<LocalizationMap>();

    // ���[�J���i��Publish�j�ێ�
    private readonly List<ARAnchor> localAnchors = new();
    // ����Storage�ۑ��ς݂��̖ڈ�iTrackableId�j
    private readonly HashSet<TrackableId> storedIds = new();

    // ---------- Unity lifecycle ----------
    private async void Start()
    {
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        if (mainCamera == null) mainCamera = Camera.main;

        await WaitSubsystems();

        // Feature ���擾�iOpenXR �ݒ�ŗL���ɂȂ��Ă���K�v����j
        mapFeature = OpenXRSettings.Instance.GetFeature<MagicLeapLocalizationMapFeature>();
        storageFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();
        if (mapFeature == null || storageFeature == null)
        {
            Log("OpenXR Features (LocalizationMap / SpatialAnchorsStorage) ���L���ł͂���܂���B");
            enabled = false; return;
        }

        var loader = XRGeneralSettings.Instance.Manager.activeLoader;
        mlAnchorSubsystem = loader?.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;

        // AR Foundation 6: trackablesChanged ���w��
        if (anchorManager != null)
            anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);

        // Storage ���̃R�[���o�b�N
        storageFeature.OnPublishComplete += OnPublishComplete;
        storageFeature.OnQueryComplete += OnQueryComplete;
        storageFeature.OnCreationCompleteFromStorage += OnCreationCompleteFromStorage;
        storageFeature.OnDeletedComplete += OnAnchorDeleteComplete;

        // UI �̃C�x���g
        if (localizeButton) { localizeButton.onClick.RemoveAllListeners(); localizeButton.onClick.AddListener(LocalizeSelectedMap); }
        if (spawnAnchorButton) { spawnAnchorButton.onClick.RemoveAllListeners(); spawnAnchorButton.onClick.AddListener(SpawnLocalAnchor); }
        if (publishButton) { publishButton.onClick.RemoveAllListeners(); publishButton.onClick.AddListener(PublishTrackingLocals); }
        if (queryButton) { queryButton.onClick.RemoveAllListeners(); queryButton.onClick.AddListener(RestoreFromStorageNearby); }
        if (deleteNearestButton) { deleteNearestButton.onClick.RemoveAllListeners(); deleteNearestButton.onClick.AddListener(DeleteNearestStored); }
        if (deleteAllButton) { deleteAllButton.onClick.RemoveAllListeners(); deleteAllButton.onClick.AddListener(DeleteAllStored); }

        // �}�b�v�ꗗ�̏�����
        InitMaps();
        UpdateLocalizationUi();
        if (publishButton) publishButton.interactable = false; // Localized �O��OFF
    }

    private void OnDestroy()
    {
        if (anchorManager != null)
            anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

        if (storageFeature != null)
        {
            storageFeature.OnPublishComplete -= OnPublishComplete;
            storageFeature.OnQueryComplete -= OnQueryComplete;
            storageFeature.OnCreationCompleteFromStorage -= OnCreationCompleteFromStorage;
            storageFeature.OnDeletedComplete -= OnAnchorDeleteComplete;
        }

        if (mapFeature != null)
            mapFeature.EnableLocalizationEvents(false);
    }

    private async Task WaitSubsystems()
    {
        while (XRGeneralSettings.Instance == null ||
               XRGeneralSettings.Instance.Manager == null ||
               XRGeneralSettings.Instance.Manager.activeLoader == null)
            await Task.Yield();
    }

    private void InitMaps()
    {
        // �[������ Space �ꗗ���擾���ăh���b�v�_�E���ɔ��f
        if (mapFeature.GetLocalizationMapsList(out mapList) == XrResult.Success && mapsDropdown)
        {
            mapsDropdown.ClearOptions();
            mapsDropdown.AddOptions(mapList.Select(m => m.Name).ToList());
        }
        var res = mapFeature.EnableLocalizationEvents(true);
        if (res != XrResult.Success) Log("EnableLocalizationEvents ���s: " + res);
    }

    // ---------- Buttons ----------
    public void LocalizeSelectedMap()
    {
        if (mapList.Length == 0) { Log("���[�J���C�Y�Ώۂ� Map ������܂���"); return; }
        string uuid = mapList[Mathf.Clamp(mapsDropdown?.value ?? 0, 0, mapList.Length - 1)].MapUUID;

        var res = mapFeature.RequestMapLocalization(uuid);
        if (res != XrResult.Success) { Log("Localize ���s: " + res); return; }

        // �V�[���̊��� Anchor �\����|��
        foreach (var a in FindObjectsByType<ARAnchor>(FindObjectsSortMode.None))
            Destroy(a.gameObject);
        localAnchors.Clear();
        storedIds.Clear();

        Log("���[�J���C�Y�v��: " + mapList[mapsDropdown?.value ?? 0].Name);
        UpdateLocalizationUi();
    }

    public void SpawnLocalAnchor()
    {
        // �������i�E�� or �J�����j
        Transform src = (controllerObject != null) ? controllerObject.transform :
                        (mainCamera != null ? mainCamera.transform : null);
        if (src == null) { Log("�������� Transform �����ݒ�ł�"); return; }

        Pose p = new Pose(src.position + src.forward * forwardMeters,
                          Quaternion.LookRotation(Vector3.ProjectOnPlane(src.forward, Vector3.up), Vector3.up));

        // �C�ӂ� GameObject ��u���AARAnchor ��t�^�iARF �̐�����@�j:contentReference[oaicite:4]{index=4}
        var root = new GameObject("ML2_Anchor");
        root.transform.SetPositionAndRotation(p.position, p.rotation);
        var anchor = root.AddComponent<ARAnchor>();
        localAnchors.Add(anchor);

        // �����ځiPrefab�j���q��
        if (anchorVisualPrefab != null)
        {
            var vis = Instantiate(anchorVisualPrefab, anchor.transform);
            TintAll(vis, Color.grey); // ���ۑ��͊D
        }

        Log("���[�J���A���J�[�𐶐��i���ۑ��j");
    }

    public void PublishTrackingLocals()
    {
        // Publish �� Localized ��Ԃ��O��i��F�����[�J���C�Y���Ǝ��s�j:contentReference[oaicite:5]{index=5}
        mapFeature.GetLatestLocalizationMapData(out LocalizationEventData d);
        if (d.State != LocalizationMapState.Localized)
        {
            Log("Publish �ɂ� Localized ���K�v�ł�"); return;
        }

        var targets = localAnchors
            .Where(a => a != null && a.trackingState == TrackingState.Tracking && !a.pending)
            .ToList();

        if (targets.Count == 0) { Log("Publish �ΏۂȂ��iTracking���̃��[�J�����K�v�j"); return; }

        // �����Ȃ��i0 �b�j�ŉi�����B������ OnPublishComplete�B:contentReference[oaicite:6]{index=6}
        storageFeature.PublishSpatialAnchorsToStorage(targets, 0);
        Log($"Publish �v��: {targets.Count} ��");
    }

    public void RestoreFromStorageNearby()
    {
        if (storageFeature == null) { Log("Storage Feature �����p�ł��܂���"); return; }

        Vector3 origin = (controllerObject != null) ? controllerObject.transform.position :
                         (mainCamera != null ? mainCamera.transform.position : Vector3.zero);

        // �ߖT�̕ۑ��ς݃A���J�[ ID ��₢���킹�i��F���a10m�j�� OnQueryComplete �ցB:contentReference[oaicite:7]{index=7}
        if (!storageFeature.QueryStoredSpatialAnchors(origin, 10f))
            Log("Query ���s");
        else
            Log("Query �v���i10m�ȓ��j");
    }

    public void DeleteNearestStored()
    {
        var a = FindClosestStoredAnchorNear(GetRefPosition(), 100f);
        if (a == null) { Log("�ߖT�̕ۑ��ς݃A���J�[��������܂���"); return; }

        string id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
        if (string.IsNullOrEmpty(id)) { Log("MapPositionId �擾���s"); return; }

        storageFeature.DeleteStoredSpatialAnchors(new List<string> { id }); // ������ OnAnchorDeleteComplete�B:contentReference[oaicite:8]{index=8}
        Log("�폜�v��: " + id);
    }

    public void DeleteAllStored()
    {
        var ids = new List<string>();
        foreach (var a in anchorManager.trackables)
        {
            string id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        if (ids.Count == 0) { Log("�폜�Ώۂ̕ۑ��ς݃A���J�[������܂���"); return; }

        storageFeature.DeleteStoredSpatialAnchors(ids);
        Log($"�ꊇ�폜�v��: {ids.Count} ��");
    }

    // ---------- Storage callbacks ----------
    private void OnPublishComplete(ulong anchorId, string anchorMapPositionId)
    {
        // Publish �ɐ������� Anchor ���u�ۑ��ς݈����v��
        ARAnchor anchor = FindAnchorByMapPositionId(anchorMapPositionId);
        if (anchor == null && mlAnchorSubsystem != null)
        {
            // �߂����[�J�����E��
            Pose p = mlAnchorSubsystem.GetAnchorPoseFromId(anchorId);
            anchor = FindClosestLocalAnchor(p.position, 0.05f);
        }

        if (anchor != null)
        {
            storedIds.Add(anchor.trackableId);
            localAnchors.Remove(anchor);
            TintAll(anchor.gameObject, Color.white); // �ۑ��ς�=��
            Log($"Publish ����: {anchorMapPositionId}");
        }
        else
        {
            Log($"Publish �����iAnchor������j: {anchorMapPositionId}");
        }
    }

    private void OnQueryComplete(List<string> anchorMapPositionIds)
    {
        if (anchorMapPositionIds == null || anchorMapPositionIds.Count == 0)
        {
            Log("Query: ������܂���ł���"); return;
        }

        // ���łɃV�[���ɑ��݂�����̂����O
        var existing = new HashSet<string>();
        foreach (var a in anchorManager.trackables)
        {
            string id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(id)) existing.Add(id);
        }

        var toCreate = anchorMapPositionIds.Where(id => !existing.Contains(id)).ToList();
        if (toCreate.Count > 0)
        {
            // ID ���� Anchor �𐶐��i�����j�B�������ʂ� trackablesChanged/OnCreationComplete �ŏE���B:contentReference[oaicite:9]{index=9}
            if (!storageFeature.CreateSpatialAnchorsFromStorage(toCreate))
                Log("CreateSpatialAnchorsFromStorage ���s");
            else
                Log($"�����v��: {toCreate.Count} ��");
        }
    }

    private void OnCreationCompleteFromStorage(Pose pose, ulong anchorId, string anchorMapPositionId, XrResult result)
    {
        if (result != XrResult.Success) { Log($"�������s: {result}"); return; }

        // ����(ARAnchor)�� trackablesChanged(added/updated) �ŗ���
        Log($"�����ʒm: {anchorMapPositionId}");
    }

    private void OnAnchorDeleteComplete(List<string> deletedMapPositionIds)
    {
        // �V�[����̊Y�� Anchor ���폜
        foreach (var id in deletedMapPositionIds)
        {
            var a = FindAnchorByMapPositionId(id);
            if (a != null) Destroy(a.gameObject);
        }
        Log($"�폜����: {deletedMapPositionIds.Count} ��");
    }

    // ---------- ARF6: trackablesChanged ----------
    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> changes)
    {
        // �ǉ��F�ۑ��ς݂Ȃ甒�A���ۑ��Ȃ�D
        foreach (var a in changes.added)
        {
            string mapPosId = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            bool isStored = !string.IsNullOrEmpty(mapPosId);
            EnsureVisual(a, isStored);
            if (isStored) { storedIds.Add(a.trackableId); TintAll(a.gameObject, Color.white); }
            else { TintAll(a.gameObject, Color.grey); if (!localAnchors.Contains(a)) localAnchors.Add(a); }
        }

        // �X�V�F�ۑ��ς݂֏��i���Ă�����\���ؑ�
        foreach (var a in changes.updated)
        {
            string mapPosId = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (!string.IsNullOrEmpty(mapPosId) && !storedIds.Contains(a.trackableId))
            {
                storedIds.Add(a.trackableId);
                localAnchors.Remove(a);
                EnsureVisual(a, true);
                TintAll(a.gameObject, Color.white);
            }
        }

        // �폜�FKeyValuePair<TrackableId, ARAnchor>�iARF6 �d�l�j:contentReference[oaicite:10]{index=10}
        foreach (var kv in changes.removed)
        {
            localAnchors.Remove(kv.Value);
            storedIds.Remove(kv.Key);
        }

        UpdateLocalizationUi();
    }

    // ---------- helpers ----------
    private void EnsureVisual(ARAnchor a, bool stored)
    {
        if (a == null || anchorVisualPrefab == null) return;
        if (a.GetComponentInChildren<MeshRenderer>() == null)
        {
            var vis = Instantiate(anchorVisualPrefab, a.transform);
            TintAll(vis, stored ? Color.white : Color.grey);
        }
    }
    private void TintAll(GameObject go, Color c)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.material.color = c;
    }

    private ARAnchor FindAnchorByMapPositionId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var a in anchorManager.trackables)
            if (mlAnchorSubsystem != null && mlAnchorSubsystem.GetAnchorMapPositionId(a) == id)
                return a;
        return null;
    }

    private ARAnchor FindClosestLocalAnchor(Vector3 pos, float maxDist)
    {
        ARAnchor best = null; float bestD = maxDist;
        foreach (var a in localAnchors)
        {
            if (a == null) continue;
            float d = Vector3.Distance(a.transform.position, pos);
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best;
    }

    private ARAnchor FindClosestStoredAnchorNear(Vector3 pos, float maxDist)
    {
        ARAnchor best = null; float bestD = maxDist;
        foreach (var a in anchorManager.trackables)
        {
            string id = mlAnchorSubsystem != null ? mlAnchorSubsystem.GetAnchorMapPositionId(a) : "";
            if (string.IsNullOrEmpty(id)) continue; // �ۑ��ς݂̂�
            float d = Vector3.Distance(a.transform.position, pos);
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best;
    }

    private Vector3 GetRefPosition()
    {
        return (controllerObject != null) ? controllerObject.transform.position :
               (mainCamera != null ? mainCamera.transform.position : Vector3.zero);
    }

    private void UpdateLocalizationUi()
    {
        if (mapFeature == null) return;

        mapFeature.GetLatestLocalizationMapData(out LocalizationEventData d);
        if (localizationLabel)
        {
            string info = $"State:{d.State} Conf:{d.Confidence}";
            if (d.State == LocalizationMapState.Localized && d.Map.Name != null)
                info += $"  Name:{d.Map.Name}";
            localizationLabel.text = info;
        }
        if (publishButton) publishButton.interactable = (d.State == LocalizationMapState.Localized);
    }

    private void Log(string msg)
    {
        if (statusLabel) statusLabel.text = msg;
        Debug.Log("[MLAnchorsUiController] " + msg);
    }
}
