// Assets/Scripts/AnchorPersistenceController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.NativeTypes;

using MagicLeap.OpenXR.Subsystems;
using MagicLeap.OpenXR.Features.SpatialAnchors;
using MagicLeap.OpenXR.Features.LocalizationMaps;

public class AnchorPersistenceController : MonoBehaviour
{
    [Header("Scene refs")]
    [SerializeField] private ARAnchorManager anchorManager;        // XR Origin �ɕt���� ARAnchorManager
    [SerializeField] private Transform headTransform;              // Main Camera�iXR Origin�z���j
    [SerializeField] private Transform rightControllerTransform;   // �E��R���g���[���i�C�Ӂj
    [SerializeField] private float restoreQueryRadiusMeters = 5f;  // �����̒T�����a

    [Header("Anchor Visual")]
    [Tooltip("ARAnchorManager.anchorPrefab �ɂ��������̂�ݒ肵�Ă����ƁA�������Ɏ����Ō����ڂ��o�܂�")]
    [SerializeField] private GameObject anchorVisualPrefab;        // �ڈ�i��Cube���j

    // ML OpenXR features
    private MagicLeapSpatialAnchorsStorageFeature storageFeature;
    private MagicLeapLocalizationMapFeature mapFeature;
    private MLXrAnchorSubsystem anchorSubsystem;

    // --- �i���Ǘ� ---
    private const string SavedIdListKey = "ML2_SavedAnchorMapPositionIds";
    private readonly HashSet<string> savedMapIds = new();             // ���̃A�v���� Publish ����/�������� Storage ��ID�i�d�������̗}�~�ɂ��g�p�j
    private readonly Dictionary<string, TrackableId> idToTrackable = new(); // Storage ID -> TrackableId�i�폜���̃V�[���|���Ɏg�p�j

    private Coroutine initRoutine;

    private void Awake()
    {
        if (anchorManager == null)
            anchorManager = FindFirstObjectByType<ARAnchorManager>();
    }

    private void OnEnable()
    {
        initRoutine = StartCoroutine(InitRoutine());
    }

    private void OnDisable()
    {
        if (initRoutine != null) { StopCoroutine(initRoutine); initRoutine = null; }

        if (storageFeature != null)
        {
            storageFeature.OnPublishComplete -= OnAnchorPublishComplete;
            storageFeature.OnQueryComplete -= OnAnchorQueryComplete;
            storageFeature.OnCreationCompleteFromStorage -= OnAnchorCreationFromStorage;
            storageFeature.OnDeletedComplete -= OnAnchorDeleteComplete;
        }
        if (anchorManager != null)
            anchorManager.trackablesChanged.RemoveListener(OnAnchorTrackablesChanged);
    }

    private IEnumerator InitRoutine()
    {
        // 1) �T�u�V�X�e���ҋ@�iARAnchorSubsystem=MLXrAnchorSubsystem�j
        yield return new WaitUntil(AreSubsystemsLoaded);

        // 2) ML Features ���擾���C�x���g�w��
        storageFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();
        mapFeature = OpenXRSettings.Instance.GetFeature<MagicLeapLocalizationMapFeature>();
        if (storageFeature == null || !storageFeature.enabled)
        {
            Debug.LogError("MagicLeapSpatialAnchorsStorageFeature �������ł��BOpenXR�ݒ���m�F���Ă��������B");
            enabled = false; yield break;
        }
        if (mapFeature != null && mapFeature.enabled)
            mapFeature.EnableLocalizationEvents(true); // ���[�J���C�Y��ԊĎ�

        storageFeature.OnPublishComplete += OnAnchorPublishComplete;
        storageFeature.OnQueryComplete += OnAnchorQueryComplete;
        storageFeature.OnCreationCompleteFromStorage += OnAnchorCreationFromStorage;
        storageFeature.OnDeletedComplete += OnAnchorDeleteComplete;

        // AR Foundation 6: trackablesChanged�ianchorsChanged �͔񐄏��j
        anchorManager.trackablesChanged.AddListener(OnAnchorTrackablesChanged);

        // 3) �ۑ��ς�ID�̕���
        LoadSavedMapIds();
    }

    private bool AreSubsystemsLoaded()
    {
        var mgr = XRGeneralSettings.Instance?.Manager;
        if (mgr?.activeLoader == null) return false;
        anchorSubsystem = mgr.activeLoader.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
        return anchorSubsystem != null;
    }

    // ========== UI Buttons ==========
    // �ۑ��iPublish�j: �R���g���[�� or �J������30cm���ARAnchor������Storage��Publish(������)
    public void OnClick_Save()
    {
        if (!IsLocalized())
        {
            Debug.LogWarning("�܂�Space�Ƀ��[�J���C�Y����Ă��܂���BSpaces�A�v���Ń}�b�v�쐬/���[�J���C�Y�A�܂��͖{�A�v�����Ń��[�J���C�Y���Ă��������B");
            return;
        }

        Pose pose = GetSpawnPose30cm();

        GameObject go = anchorManager.anchorPrefab != null
            ? Instantiate(anchorManager.anchorPrefab, pose.position, pose.rotation)
            : new GameObject("Anchor");

        var arAnchor = go.GetComponent<ARAnchor>() ?? go.AddComponent<ARAnchor>();
        StartCoroutine(PublishWhenTracking(arAnchor)); // Tracking �ɂȂ����� Publish
    }

    // �����iQuery��CreateFromStorage�j
    public void OnClick_Restore()
    {
        if (!IsLocalized())
        {
            Debug.LogWarning("���[�J���C�Y����Ă��܂���B�����ɂ̓��[�J���C�Y���K�v�ł��B");
            return;
        }
        var origin = headTransform != null ? headTransform.position : Camera.main.transform.position;
        storageFeature.QueryStoredSpatialAnchors(origin, Mathf.Max(restoreQueryRadiusMeters, 0.1f));
    }

    // �폜�i���̃A�v���ŕۑ������S�A���J�[���폜�j
    public void OnClick_DeleteAll()
    {
        if (savedMapIds.Count == 0)
        {
            Debug.Log("�ۑ��ς݂�Anchor�͂���܂���B");
            return;
        }
        storageFeature.DeleteStoredSpatialAnchors(savedMapIds.ToList());
        // OnDeletedComplete �� PlayerPrefs / �V�[����|��
    }

    // ========== ������ ==========

    private IEnumerator PublishWhenTracking(ARAnchor a)
    {
        while (a != null && a.trackingState != TrackingState.Tracking)
            yield return null;

        if (a == null) yield break;

        // �������� 0�i���O�t�������͕s�j
        storageFeature.PublishSpatialAnchorsToStorage(new List<ARAnchor> { a }, 0);
        // ������ OnAnchorPublishComplete �Ŏ󂯎��
    }

    private Pose GetSpawnPose30cm()
    {
        Transform t = rightControllerTransform != null ? rightControllerTransform :
                      (headTransform != null ? headTransform : Camera.main.transform);
        return new Pose(t.position + t.forward * 0.30f, t.rotation);
    }

    private bool IsLocalized()
    {
        if (mapFeature == null || !mapFeature.enabled) return true; // �����`�F�b�N�Ȃ��^�p����
        LocalizationEventData data;
        if (mapFeature.GetLatestLocalizationMapData(out data))
            return data.State == LocalizationMapState.Localized;
        return false;
    }

    // ========== Storage �R�[���o�b�N ==========

    // Publish �����FStorage �� ID ���T����i�d�����p�ɃZ�b�g�ցj
    private void OnAnchorPublishComplete(ulong anchorId, string anchorMapPositionId)
    {
        if (!string.IsNullOrEmpty(anchorMapPositionId))
        {
            savedMapIds.Add(anchorMapPositionId);
            SaveSavedMapIds();

            // Storage ID -> TrackableId ���������ċt�������T����i���݂���΁j
            try
            {
                var tid = anchorSubsystem.GetTrackableIdFromMapPositionId(anchorMapPositionId);
                if (tid != TrackableId.invalidId) idToTrackable[anchorMapPositionId] = tid;
            }
            catch { /* SDK ���قŖ��񋟂Ȃ�X�L�b�v */ }
        }

        var pose = anchorSubsystem.GetAnchorPoseFromId(anchorId);
        Debug.Log($"Publish Complete: {anchorMapPositionId} at {pose.position}");
    }

    // Query �����F�������̂��̂��� CreateFromStorage
    private void OnAnchorQueryComplete(List<string> anchorMapPositionIds)
    {
        if (anchorMapPositionIds == null || anchorMapPositionIds.Count == 0)
        {
            Debug.Log("Query: ������܂���ł����B���a���L���Ă��������B");
            return;
        }

        var toCreate = new List<string>();

        foreach (var id in anchorMapPositionIds)
        {
            // ���łɕێ����Ă��� Storage ID �̓X�L�b�v�i�d����������j
            if (savedMapIds.Contains(id))
                continue;

            // �ǉ��̏d���m�F�iID��TrackableId��ARAnchor �Ŋ��ɑ��݂Ȃ�X�L�b�v�j
            bool exists = false;
            try
            {
                var tid = anchorSubsystem.GetTrackableIdFromMapPositionId(id);
                if (tid != TrackableId.invalidId)
                {
                    var existing = anchorManager.GetAnchor(tid);
                    exists = (existing != null);
                }
            }
            catch { /* SDK���قŖ��񋟂Ȃ炱�̃`�F�b�N�̓X�L�b�v */ }

            if (!exists) toCreate.Add(id);
        }

        if (toCreate.Count > 0)
        {
            bool ok = storageFeature.CreateSpatialAnchorsFromStorage(toCreate);
            if (!ok) Debug.LogError("CreateSpatialAnchorsFromStorage ���s");
        }
    }

    // Storage ����̐��������F�����ڕt�^�{�}�b�s���O�ۑ�
    private void OnAnchorCreationFromStorage(Pose pose, ulong anchorId, string anchorMapPositionId, XrResult result)
    {
        Debug.Log($"Restore: {anchorMapPositionId} result={result}");

        if (result == XrResult.Success)
        {
            savedMapIds.Add(anchorMapPositionId);
            SaveSavedMapIds();

            // ID -> TrackableId ���T���āA�����ڂ�₤
            try
            {
                var tid = anchorSubsystem.GetTrackableIdFromMapPositionId(anchorMapPositionId);
                if (tid != TrackableId.invalidId)
                {
                    idToTrackable[anchorMapPositionId] = tid;

                    var anchor = anchorManager.GetAnchor(tid);
                    if (anchor != null && anchorVisualPrefab != null && anchor.transform.childCount == 0)
                        Instantiate(anchorVisualPrefab, anchor.transform);
                }
            }
            catch { /* SDK���� */ }
        }
    }

    // Storage ���폜�����FPlayerPrefs �ƃV�[���̊Y�� Anchor ��|��
    private void OnAnchorDeleteComplete(List<string> anchorMapPositionIds)
    {
        bool changed = false;
        foreach (var id in anchorMapPositionIds)
        {
            changed |= savedMapIds.Remove(id);

            // �V�[����̊Y�� ARAnchor ��j���i�\�Ȃ�j
            try
            {
                if (idToTrackable.TryGetValue(id, out var tid))
                {
                    var a = anchorManager.GetAnchor(tid);
                    if (a != null) Destroy(a.gameObject);
                    idToTrackable.Remove(id);
                }
                else
                {
                    var tid2 = anchorSubsystem.GetTrackableIdFromMapPositionId(id);
                    if (tid2 != TrackableId.invalidId)
                    {
                        var a2 = anchorManager.GetAnchor(tid2);
                        if (a2 != null) Destroy(a2.gameObject);
                    }
                }
            }
            catch { /* SDK���� */ }
        }

        if (changed) SaveSavedMapIds();
        Debug.Log($"Delete Complete: {anchorMapPositionIds.Count} anchor(s) removed");
    }

    // ========== AR Foundation 6: trackablesChanged ==========
    private void OnAnchorTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> e)
    {
        // �ǉ����F�����ڂ������ꍇ�����⏕�\����t�^
        foreach (var added in e.added)
        {
            if (anchorVisualPrefab != null && added.transform.childCount == 0)
                Instantiate(anchorVisualPrefab, added.transform);
        }

        // �X�V���F�����ł͓��ɉ������Ȃ��i�ۑ������ Storage ���ňꌳ�Ǘ��j

        // �폜���Fremoved �� KeyValuePair<TrackableId, ARAnchor>
        foreach (var kv in e.removed)
        {
            var tid = kv.Key;

            // TrackableId��Storage ID �̋t�����͕ێ����Ă��Ȃ����߁A��������Y���G���g�����������x�ɂƂǂ߂�
            foreach (var pair in idToTrackable.Where(p => p.Value == tid).ToList())
                idToTrackable.Remove(pair.Key);
        }
    }

    // ========== PlayerPrefs�i�ۑ�ID�̉i�����j ==========

    [Serializable] private class IdList { public List<string> ids = new(); }
    private void LoadSavedMapIds()
    {
        var json = PlayerPrefs.GetString(SavedIdListKey, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var l = JsonUtility.FromJson<IdList>(json);
            foreach (var id in l.ids) savedMapIds.Add(id);
        }
        catch { }
    }
    private void SaveSavedMapIds()
    {
        var l = new IdList { ids = savedMapIds.ToList() };
        PlayerPrefs.SetString(SavedIdListKey, JsonUtility.ToJson(l));
        PlayerPrefs.Save();
    }
}
