// Assets/_APP/Scripts/New/Ml2BumperSpawn_InputActionUnified.cs
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Ml2BumperSpawn_InputActionUnified : MonoBehaviour
{
    [SerializeField] private ML2AnchorsControllerUnified controller;
    [SerializeField] private InputAction bumperAction;     // Inspector ���ݒ�ł���������
    [SerializeField] private int cooldownMs = 250;
    [SerializeField] private bool preferRightHand = true;

#if ENABLE_INPUT_SYSTEM
    private double _last;

    private void OnEnable()
    {
        if (controller == null)
#if UNITY_6000_0_OR_NEWER
            controller = Object.FindFirstObjectByType<ML2AnchorsControllerUnified>();
#else
      controller = FindObjectOfType<ML2AnchorsControllerUnified>();
#endif

        if (bumperAction == null)
        {
            // ML2 �� Bumper �� gripPressed
            string hand = preferRightHand ? "{RightHand}" : "{LeftHand}";
            string path = $"<MagicLeapController>{hand}/gripPressed";
            bumperAction = new InputAction("ML2_Bumper", InputActionType.Button, path);
        }

        bumperAction.started += OnBumper;   // �����u��
        bumperAction.performed += OnBumper;   // PressInteraction ���ł��E����悤�ی�
        bumperAction.Enable();
    }

    private void OnDisable()
    {
        if (bumperAction != null)
        {
            bumperAction.started -= OnBumper;
            bumperAction.performed -= OnBumper;
            bumperAction.Disable();
        }
    }

    private void OnBumper(InputAction.CallbackContext ctx)
    {
        if (controller == null) return;
        if (ctx.time - _last < (cooldownMs / 1000.0)) return;
        _last = ctx.time;

        // �� �C���_�F���݂��Ȃ� SpawnFromControllerInput() �ł͂Ȃ��A���̂� Async ���\�b�h���Ă�
        _ = controller.SpawnLocalAnchorAsync();
    }
#else
  private void OnEnable() {
    Debug.LogWarning("New Input System �������ł��BProject Settings > Player > Active Input Handling ���m�F���Ă��������B");
  }
#endif
}
