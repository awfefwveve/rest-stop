using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RestStop
{
    /// <summary>
    /// 1인칭 이동, 시점, 바라보기, 상호작용, 들기/놓기.
    /// 이동은 소유자가 직접 처리하고, 상호작용 판정은 호스트에 요청한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController Local { get; private set; }
        public static readonly List<PlayerController> All = new();
        public static bool UIOpen;
        public static int UIClosedFrame = -1;

        public Camera cam;
        public Transform holdPoint;
        public GameObject bodyVisual;   // 다른 사람에게 보이는 몸 (내 화면에선 숨김)

        public float walkSpeed = 3.5f;
        public float sprintSpeed = 6f;
        public float mouseSensitivity = 0.12f;
        public float padSensitivity = 160f;
        public float reach = 2.6f;
        public float repeatInterval = 0.15f;

        public NetworkVariable<ulong> HeldId = new(Carryable.None);

        CharacterController _cc;
        float _pitch, _vy, _repeat;

        public Interactable Hover { get; private set; }
        public string HoverPrompt { get; private set; } = "";

        public Carryable Held
        {
            get
            {
                if (HeldId.Value == Carryable.None || NetworkManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(HeldId.Value, out var no)
                    ? no.GetComponent<Carryable>() : null;
            }
        }

        void Awake() => _cc = GetComponent<CharacterController>();

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            bool mine = IsOwner;
            cam.enabled = mine;
            var listener = cam.GetComponent<AudioListener>();
            if (listener) listener.enabled = mine;
            if (bodyVisual) bodyVisual.SetActive(!mine);

            if (mine)
            {
                Local = this;
                var spawn = LevelLayout.Instance ? LevelLayout.Instance.playerSpawn : null;
                if (spawn)
                {
                    _cc.enabled = false;
                    transform.SetPositionAndRotation(
                        spawn.position + spawn.right * (OwnerClientId % 4) * 1.2f, spawn.rotation);
                    _cc.enabled = true;
                }
                LockCursor(true);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer) ServerDrop();
            if (Local == this) Local = null;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (!IsOwner) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame && !UIOpen && Time.frameCount != UIClosedFrame)
                LockCursor(Cursor.lockState != CursorLockMode.Locked);

            bool canAct = !UIOpen && Cursor.lockState == CursorLockMode.Locked;

            // 시점
            if (canAct)
            {
                Vector2 look = Vector2.zero;
                if (mouse != null) look += mouse.delta.ReadValue() * mouseSensitivity;
                if (pad != null) look += pad.rightStick.ReadValue() * padSensitivity * Time.deltaTime;
                transform.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -80f, 80f);
                cam.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            // 이동
            Vector2 move = Vector2.zero;
            bool sprint = false;
            if (canAct)
            {
                if (kb != null)
                {
                    move.x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                    move.y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                    sprint = kb.leftShiftKey.isPressed;
                }
                if (pad != null)
                {
                    move += pad.leftStick.ReadValue();
                    sprint |= pad.leftStickButton.isPressed;
                }
            }
            move = Vector2.ClampMagnitude(move, 1f);
            _vy = _cc.isGrounded ? -1f : _vy - 20f * Time.deltaTime;
            var dir = transform.right * move.x + transform.forward * move.y;
            _cc.Move((dir * (sprint ? sprintSpeed : walkSpeed) + Vector3.up * _vy) * Time.deltaTime);

            // 바라보기
            UpdateHover();
            if (!canAct) return;

            bool primaryDown = (mouse != null && mouse.leftButton.wasPressedThisFrame)
                               || (kb != null && kb.eKey.wasPressedThisFrame)
                               || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool primaryHeld = (mouse != null && mouse.leftButton.isPressed)
                               || (kb != null && kb.eKey.isPressed)
                               || (pad != null && pad.buttonSouth.isPressed);
            bool secondaryDown = (mouse != null && mouse.rightButton.wasPressedThisFrame)
                                 || (kb != null && kb.rKey.wasPressedThisFrame)
                                 || (pad != null && pad.buttonWest.wasPressedThisFrame);
            bool dropDown = (kb != null && kb.gKey.wasPressedThisFrame)
                            || (pad != null && pad.buttonEast.wasPressedThisFrame);

            if (primaryDown)
            {
                TryInteract(InteractKind.Primary);
                _repeat = repeatInterval * 2f;
            }
            else if (primaryHeld && Hover != null && Hover.RepeatOnHold)
            {
                _repeat -= Time.deltaTime;
                if (_repeat <= 0f) { _repeat = repeatInterval; TryInteract(InteractKind.Primary); }
            }
            if (secondaryDown) TryInteract(InteractKind.Secondary);
            if (dropDown) DropRpc();
        }

        void UpdateHover()
        {
            Hover = null;
            HoverPrompt = "";
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, reach, ~0, QueryTriggerInteraction.Collide)) return;
            var it = hit.collider.GetComponentInParent<Interactable>();
            if (it == null || !it.IsSpawned) return;
            Hover = it;
            HoverPrompt = it.GetPrompt(this);
        }

        void TryInteract(InteractKind kind)
        {
            if (Hover == null) return;
            if (Hover.LocalOnly) { Hover.LocalInteract(this, kind); return; }
            InteractRpc(new NetworkBehaviourReference(Hover), kind);
        }

        [Rpc(SendTo.Server)]
        void InteractRpc(NetworkBehaviourReference target, InteractKind kind, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            if (!target.TryGet(out Interactable it)) return;
            // 너무 먼 대상은 무시 (간단한 치트 방지)
            if (Vector3.Distance(it.transform.position, transform.position) > reach + 3f) return;
            it.ServerInteract(this, kind);
        }

        [Rpc(SendTo.Server)]
        void DropRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            ServerDrop();
        }

        // ---- 호스트 전용 ----

        public void ServerHold(Carryable c)
        {
            if (HeldId.Value != Carryable.None || c.IsHeld) return;
            HeldId.Value = c.NetworkObjectId;
            c.ServerSetHolder(NetworkObjectId);
        }

        public void ServerDrop()
        {
            var c = Held;
            HeldId.Value = Carryable.None;
            if (c == null) return;
            // 손 앞에서 바닥으로 내려 놓기
            var from = holdPoint.position;
            var pos = from;
            if (Physics.Raycast(from, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.01f;
            c.ServerPlace(pos, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }

        /// <summary>들고 있던 물건이 소모됐을 때 (쓰레기 버림 등).</summary>
        public void ServerClearHeld() => HeldId.Value = Carryable.None;
    }
}
