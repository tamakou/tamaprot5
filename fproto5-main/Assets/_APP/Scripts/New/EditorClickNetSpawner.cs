#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.EventSystems;          // �N���b�N��UI�ォ�̔���Ɏg�p�i�C�Ӂj
using Fusion;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;           // �VInput System
#endif

/// <summary>
/// Editor����F���N���b�N�� NetworkObject �� Runner.Spawn() ����ȈՃf�o�b�K
/// </summary>
public class EditorClickNetSpawner : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera cam;
    [SerializeField] private NetworkObject spawnPrefab;      // NetworkObject�t����Prefab�iPrefab Table�o�^�K�{�j
    [SerializeField] private LayerMask rayMask = ~0;         // �N���b�N���Collider�������ꍇ�̂��߂ɒ�����
    [SerializeField] private float fallbackDistance = 1.5f;  // �����薳�����A�J�����O���ɏo������

    private NetworkRunner _runner;

    private void Awake()
    {
        if (!cam) cam = Camera.main;
    }

    private void Update()
    {
        // UI��̃N���b�N�͖����i�C�Ӂj
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject())
            return;

        // ���N���b�N���o
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return;
#else
        if (!Input.GetMouseButtonDown(0))
            return;
#endif
        TrySpawnAtMouse();
    }

    private void TrySpawnAtMouse()
    {
        // Runner �𓮓I�擾�i������Ȃ�/���N���Ȃ璆�f�j
        if (_runner == null) _runner = FindAnyObjectByType<NetworkRunner>();
        if (_runner == null || !_runner.IsRunning) { Debug.LogWarning("[EditorClickNetSpawner] Runner not running."); return; }
        if (spawnPrefab == null) { Debug.LogError("[EditorClickNetSpawner] Prefab not assigned."); return; }

        // �}�E�X�ʒu �� 3D���C
        Vector3 screenPos;
#if ENABLE_INPUT_SYSTEM
        screenPos = Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Input.mousePosition;
#else
        screenPos = Input.mousePosition;
#endif
        Ray ray = cam ? cam.ScreenPointToRay(screenPos) : new Ray(Vector3.zero, Vector3.forward);

        Vector3 pos; Quaternion rot;
        if (Physics.Raycast(ray, out var hit, 100f, rayMask))
        {
            pos = hit.point;
            var fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            rot = Quaternion.LookRotation(fwd, Vector3.up);
        }
        else
        {
            pos = (cam ? cam.transform.position : Vector3.zero) + (cam ? cam.transform.forward : Vector3.forward) * fallbackDistance;
            rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cam ? cam.transform.forward : Vector3.forward, Vector3.up), Vector3.up);
        }

        // �� Shared���[�h�Ȃ�N���C�A���g����Spawn�� / Host�EServer���[�h�̓T�[�o��Spawn����
        _runner.Spawn(spawnPrefab, pos, rot, _runner.LocalPlayer);
    }
}
#endif
