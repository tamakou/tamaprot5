using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

public class ML2PermissionBootstrap : MonoBehaviour
{
  // ���ۂɃ����^�C���v�����K�v�� �gDangerous�h ����������
  // �� SPATIAL_MAPPING �̓��b�V�������g���������K�v�B�s�v�Ȃ�z�񂩂�O���B
  static readonly string[] DangerousPermissions =
  {
        "com.magicleap.permission.SPACE_IMPORT_EXPORT",
        // "com.magicleap.permission.SPATIAL_MAPPING",
    };

  void Start()
  {
#if UNITY_ANDROID && !UNITY_EDITOR
        EnsurePermissions();
#endif
  }

  static void EnsurePermissions()
  {
    var toRequest = new List<string>();
    foreach (var p in DangerousPermissions)
    {
      if (!Permission.HasUserAuthorizedPermission(p))
      {
        // Android���u�ǉ�������\�����ׂ��v�Ɣ��f���Ă��邩�ǂ���
        var shouldExplain = Permission.ShouldShowRequestPermissionRationale(p);
        if (shouldExplain)
        {
          Debug.Log($"[Perm] Rationale for {p}. You may want to show an explainer UI here.");
        }
        toRequest.Add(p);
      }
    }

    if (toRequest.Count == 0)
    {
      Debug.Log("[Perm] All dangerous permissions already granted.");
      return;
    }

    var cb = new PermissionCallbacks();
    cb.PermissionGranted += (perm) =>
    {
      Debug.Log($"[Perm] Granted: {perm}");
    };
    cb.PermissionDenied += (perm) =>
    {
      Debug.LogWarning($"[Perm] Denied: {perm}");
      // �g����\�����Ȃ��h�������ǂ����́A�ēx ShouldShow... �����Ĕ��f
      var canAskAgain = Permission.ShouldShowRequestPermissionRationale(perm);
      if (!canAskAgain)
      {
        Debug.LogWarning($"[Perm] {perm} set to Don't Ask Again? Opening App Settings...");
        OpenAppSettings(); // �ݒ��ʂ֗U��
      }
    };

    // �� �񐄏��FPermissionDeniedAndDontAskAgain �͐V���߂�Android�ŕs����
    // �iUnity������ PermissionDenied �̂ݍw�ǂ𐄏��j
    Permission.RequestUserPermissions(toRequest.ToArray(), cb);
  }

  // �A�v���̏ڍאݒ��ʂ��J���i���[�U�[�Ɏ蓮�Ō�����ON�ɂ��Ă��炤�j
  static void OpenAppSettings()
  {
    try
    {
      using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
      using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
      using (var uriClass = new AndroidJavaClass("android.net.Uri"))
      using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS"))
      {
        string pkg = activity.Call<string>("getPackageName");
        var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", pkg, null);
        intent.Call<AndroidJavaObject>("setData", uri);
        intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
        activity.Call("startActivity", intent);
      }
    }
    catch (System.Exception e)
    {
      Debug.LogError($"[Perm] Failed to open App Settings: {e.Message}");
    }
  }
}
