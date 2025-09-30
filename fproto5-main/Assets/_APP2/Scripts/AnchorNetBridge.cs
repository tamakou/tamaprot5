// Assets/_APP/Scripts/AnchorNetBridge.cs
using Fusion;
using UnityEngine;

public class AnchorNetBridge : NetworkBehaviour
{
    public static AnchorNetBridge Instance { get; private set; }
    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    // ���JAPI�F�C�ӂ̃N���C�A���g����Ăׂ�
    public void BroadcastAnchorPose(Vector3 pos, Quaternion rot, string mapUUID)
    {
        // Host / Server �o�R�őS���֒��p�iFusion�����̓�iRPC�j
        RPC_SendToAuthority(pos, rot, mapUUID);
    }

    // ���͌��� �� ��Ԍ����֑��M
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    void RPC_SendToAuthority(Vector3 pos, Quaternion rot, string mapUUID, RpcInfo info = default)
    {
        RPC_RelayToAll(pos, rot, mapUUID);
    }

    // ��Ԍ��� �� �S���֔z�M
    [Rpc(RpcSources.StateAuthority, RpcTargets.All, HostMode = RpcHostMode.SourceIsServer)]
    void RPC_RelayToAll(Vector3 pos, Quaternion rot, string mapUUID)
    {
        // ��M���� ML2AnchorsControllerUnified.OnNetworkAnchorPublished ���Ă�
        ML2AnchorsControllerUnified.Instance?.OnNetworkAnchorPublished(pos, rot, mapUUID);
    }
}
