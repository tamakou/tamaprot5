// Assets/_APP/Scripts/grab/Ml2NetworkGrabbable.cs
using System;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class Ml2NetworkGrabbable : NetworkBehaviour, IStateAuthorityChanged
{
    [Header("Behaviour")]
    [SerializeField] bool expectedIsKinematicAtRest = true;   // ����������̊���
    [SerializeField] bool applyReleaseVelocity = true;    // �����u�ԂɎ�̑��x��^����
    [SerializeField] float followLerp = 25f;     // �Ǐ]�X���[�Y

    Rigidbody _rb;

    // �͂ݒ��̂݁i���[�J���j�Ŏg���Ǐ]�^�[�Q�b�g
    Transform _followTarget;
    Vector3 _localPosOffset;
    Quaternion _localRotOffset;

    // Release �p�ɐ��肷����`/�p���x�iStateAuthority ���ōX�V�j
    Vector3 _lastPos, _vel, _angVel;
    Quaternion _lastRot;

    [Networked, OnChangedRender(nameof(OnGrabChanged))]
    public NetworkBool NetIsGrabbed { get; private set; }   // �͂ݏ�ԁi�S���ɓ����j  :contentReference[oaicite:3]{index=3}

    [Networked] public PlayerRef Holder { get; private set; } // �͂ݎ�i���p�j

    void Awake()
    {
        _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = expectedIsKinematicAtRest;
        _rb.useGravity = false;
        _lastPos = transform.position;
        _lastRot = transform.rotation;
    }

    // ===== �葤�i�R���g���[���j����Ă�API =====

    /// <summary>�͂ݗv���i�o���p�[�Ȃǂ���Ăԁj</summary>
    public void RequestGrab(Transform hand)
    {
        if (NetIsGrabbed || hand == null) return;

        // ������v���iNetworkObject �� Allow State Authority Override �� ON �Ɂj
        if (!Object.HasStateAuthority)
            Object.RequestStateAuthority();      // �����擾�͎�Tick�Ŕ��f�B:contentReference[oaicite:4]{index=4}

        _followTarget = hand;                     // ��������ꂽTick�Ŋm��iFixedUpdateNetwork�j
    }

    /// <summary>����</summary>
    public void RequestRelease()
    {
        if (!NetIsGrabbed) return;

        if (Object.HasStateAuthority)
        {
            NetIsGrabbed = false;
            Holder = default;
            Object.ReleaseStateAuthority();      // ���̐l���͂߂�悤�ԋp�i�p�r�ŏȗ��j�B:contentReference[oaicite:5]{index=5}
        }
        _followTarget = null;
    }

    // ===== Fusion =====

    public override void FixedUpdateNetwork()
    {
        // ���������A���܂� Grab ���m�肵�Ă��Ȃ��ꍇ�ɏ�����
        if (Object.HasStateAuthority && _followTarget != null && !NetIsGrabbed)
        {
            _localPosOffset = _followTarget.InverseTransformPoint(transform.position);
            _localRotOffset = Quaternion.Inverse(_followTarget.rotation) * transform.rotation;
            NetIsGrabbed = true;
            Holder = Runner.LocalPlayer;
        }

        // �͂ݒ��FStateAuthority ���������Ǐ]�BNetworkTransform ���z�M���đ��[���֔��f
        if (Object.HasStateAuthority && NetIsGrabbed && _followTarget != null)
        {
            var targetPos = _followTarget.TransformPoint(_localPosOffset);
            var targetRot = _followTarget.rotation * _localRotOffset;

            var dt = Mathf.Max(Runner.DeltaTime, 1e-5f);
            _vel = (targetPos - _lastPos) / dt;
            _angVel = AngularVelocity(_lastRot, targetRot, dt);
            _lastPos = targetPos;
            _lastRot = targetRot;

            if (followLerp > 0f)
            {
                transform.position = Vector3.Lerp(transform.position, targetPos, 1 - Mathf.Exp(-followLerp * dt));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1 - Mathf.Exp(-followLerp * dt));
            }
            else
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
            }
        }
    }

    // Fusion 2 �̌����ω��t�b�N�� override �ł͂Ȃ� IStateAuthorityChanged ���������� :contentReference[oaicite:6]{index=6}
    public void StateAuthorityChanged()
    {
        if (!Object.HasStateAuthority)
            _followTarget = null;   // �����r�����Ƀt�H���[����
    }

    // ===== OnChangedRender �R�[���o�b�N�i�S�N���C�A���g�j =====  :contentReference[oaicite:7]{index=7}
    // Networked �v���p�e�B NetIsGrabbed ���ω������Ƃ��Ă΂��
    void OnGrabChanged()
    {
        if (NetIsGrabbed)
        {
            if (_rb) _rb.isKinematic = true;    // �͂ݒ��͕�����~
        }
        else
        {
            if (_rb)
            {
                _rb.isKinematic = expectedIsKinematicAtRest;

#if UNITY_6000_0_OR_NEWER
                if (applyReleaseVelocity && Object.HasStateAuthority && !expectedIsKinematicAtRest)
                {
                    _rb.linearVelocity = _vel;   // Unity 6 �� linearVelocity ���g�p :contentReference[oaicite:8]{index=8}
                    _rb.angularVelocity = _angVel;
                }
#else
                if (applyReleaseVelocity && Object.HasStateAuthority && !expectedIsKinematicAtRest)
                {
                    _rb.velocity        = _vel;  // ���o�[�W�����݊�
                    _rb.angularVelocity = _angVel;
                }
#endif
            }
            _followTarget = null;
        }
    }

    static Vector3 AngularVelocity(Quaternion from, Quaternion to, float dt)
    {
        if (dt <= 0f) return Vector3.zero;
        var dq = to * Quaternion.Inverse(from);
        dq.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        return axis * Mathf.Deg2Rad * angle / dt;
    }
}
