using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 들고 다닐 수 있는 물건. NetworkTransform 없이,
    /// 들고 있으면 각자 화면에서 들고 있는 사람 손에 붙이고,
    /// 내려놓으면 호스트가 정한 위치(RestPos)로 맞춘다.
    /// </summary>
    public class Carryable : Interactable
    {
        public const ulong None = ulong.MaxValue;

        public NetworkVariable<ulong> HolderId = new(None);
        public NetworkVariable<Vector3> RestPos = new();
        public NetworkVariable<Quaternion> RestRot = new(Quaternion.identity);

        Collider[] _cols;
        bool _colsOn = true;

        public bool IsHeld => HolderId.Value != None;
        public virtual bool IsTrash => false;
        public virtual string DisplayName => Loc.Get("item_generic");

        protected virtual void Awake() => _cols = GetComponentsInChildren<Collider>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                RestPos.Value = transform.position;
                RestRot.Value = transform.rotation;
            }
        }

        public override string GetPrompt(PlayerController p)
        {
            if (IsHeld || p.Held != null) return "";
            return Loc.Get("prompt_pickup", DisplayName);
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (kind != InteractKind.Primary || IsHeld) return;
            p.ServerHold(this);
        }

        PlayerController Holder()
        {
            if (!IsHeld) return null;
            foreach (var p in PlayerController.All)
                if (p != null && p.NetworkObjectId == HolderId.Value) return p;
            return null;
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;
            var h = Holder();
            bool wantCols = h == null;
            if (wantCols != _colsOn)
            {
                _colsOn = wantCols;
                foreach (var c in _cols) if (c) c.enabled = wantCols;
            }

            if (h != null)
                transform.SetPositionAndRotation(h.holdPoint.position, h.holdPoint.rotation);
            else
            {
                transform.position = Vector3.Lerp(transform.position, RestPos.Value, 20f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, RestRot.Value, 20f * Time.deltaTime);
            }
        }

        // ---- 호스트 전용 ----

        public void ServerSetHolder(ulong playerObjectId) => HolderId.Value = playerObjectId;

        public void ServerPlace(Vector3 pos, Quaternion rot)
        {
            HolderId.Value = None;
            RestPos.Value = pos;
            RestRot.Value = rot;
        }

        /// <summary>쓰레기통·분리수거장 등에서 소모될 때.</summary>
        public void ServerConsume()
        {
            var h = Holder();
            if (h != null && h.HeldId.Value == NetworkObjectId) h.HeldId.Value = None;
            HolderId.Value = None;
            if (IsSpawned) NetworkObject.Despawn(true);
        }
    }
}
