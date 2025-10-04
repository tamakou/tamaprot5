using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR Foundation 6 �Ή��́u�������\���v�t�B���^
/// - ARPlaneManager.trackablesChanged �Œǉ�/�X�V���Ď�
/// - ARPlane.classifications �̃t���O�� Floor ���܂܂����̂����\��
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class FloorPlaneFilter : MonoBehaviour
{
  [SerializeField] private Material floorMaterial; // ���F�i�C�Ӂj
  [SerializeField] private bool hideNonFloor = true;

  private ARPlaneManager pm;

  private void Awake() => pm = GetComponent<ARPlaneManager>();

  private void OnEnable()
  {
    // AR Foundation 6: trackablesChanged (UnityEvent)
    pm.trackablesChanged.AddListener(OnPlanesChanged);
  }

  private void OnDisable()
  {
    pm.trackablesChanged.RemoveListener(OnPlanesChanged);
  }

  public void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
  {
    foreach (var p in args.added) Apply(p);
    foreach (var p in args.updated) Apply(p);
    // removed �͕s�v
  }

  private void Apply(ARPlane plane)
  {
    // AR Foundation 6: PlaneClassifications �� [Flags]
    bool isFloor = (plane.classifications & PlaneClassifications.Floor) != 0;

    if (hideNonFloor) plane.gameObject.SetActive(isFloor);

    if (isFloor && floorMaterial)
    {
      var r = plane.GetComponent<MeshRenderer>();
      if (r) r.material = floorMaterial;
    }
  }
}
