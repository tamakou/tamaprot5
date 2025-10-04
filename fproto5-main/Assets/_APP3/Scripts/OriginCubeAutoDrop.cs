using System.Collections;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class OriginCubeAutoDrop : NetworkBehaviour
{
  [Header("Raycast Settings")]
  [SerializeField] private LayerMask floorLayers = ~0;   // ML Meshing �̃��C���[���܂߂�
  [SerializeField] private float rayStartYOffset = 2.0f; // ���_����̃��C�J�n����
  [SerializeField] private float maxRayDistance = 6.0f;  // �����\����
  [SerializeField] private float dropSpeed = 0.8f;       // �ڎ����₷�������A�j�����x
  [SerializeField] private float skin = 0.002f;          // �߂荞�ݖh�~

  [Networked] public bool HasSnapped { get; private set; }

  private Transform _spaceOrigin;

  /// <summary>���[�J���C�Y������ɌĂ�</summary>
  public void Begin(Transform spaceOrigin)
  {
    _spaceOrigin = spaceOrigin;
    if (HasSnapped) return;
    if (Object != null && !Object.HasStateAuthority) return; // ����� StateAuthority �̂�
    StopAllCoroutines();
    StartCoroutine(DropRoutine());
  }

  private IEnumerator DropRoutine()
  {
    var col = GetComponent<Collider>();
    float halfY = col.bounds.extents.y;

    // ���_�̏����ォ�牺�������C
    Vector3 rayStart = _spaceOrigin.position + Vector3.up * rayStartYOffset;
    if (!Physics.Raycast(rayStart, Vector3.down, out var hit,
        rayStartYOffset + maxRayDistance, floorLayers, QueryTriggerInteraction.Ignore))
    {
      yield break; // �������Ȃ����͉������Ȃ�
    }

    float targetY = hit.point.y + halfY + skin;

    // �܂��L���[�u�����_�̏�����ɒu���ĉ�����
    Vector3 pos = _spaceOrigin.position + Vector3.up * rayStartYOffset;
    transform.position = pos;

    // �A�j���[�V�����Őڒn�܂ŉ�����
    while (pos.y > targetY)
    {
      pos.y = Mathf.Max(targetY, pos.y - dropSpeed * Time.deltaTime);
      transform.position = pos;
      yield return null;
    }
    transform.position = pos;

    HasSnapped = true; // �����t���O
  }

  /// <summary>�������Y�����icontentRoot�p�j</summary>
  public float GetOffsetYFromOrigin()
  {
    return transform.position.y - (_spaceOrigin ? _spaceOrigin.position.y : 0f);
  }
}
