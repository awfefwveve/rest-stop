using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RestStop
{
    /// <summary>
    /// 창고 태블릿. 누르면 내 화면에만 주문창이 뜨고, 결제는 호스트가 한다.
    /// (프로토타입은 IMGUI. 출시 전 uGUI/UI Toolkit으로 교체)
    /// </summary>
    public class OrderTerminal : Interactable
    {
        static OrderTerminal _open;
        int[] _qty;
        Vector2 _scroll;

        public override bool LocalOnly => true;
        public override string GetPrompt(PlayerController p) => Loc.Get("prompt_order");

        public override void LocalInteract(PlayerController p, InteractKind kind) => Open();

        void Open()
        {
            _open = this;
            PlayerController.UIOpen = true;
            PlayerController.LockCursor(false);
            var n = GameRoot.Instance.products.items.Count;
            if (_qty == null || _qty.Length != n) { _qty = new int[n]; for (int i = 0; i < n; i++) _qty[i] = 1; }
        }

        void Close()
        {
            _open = null;
            PlayerController.UIOpen = false;
            PlayerController.UIClosedFrame = Time.frameCount;
            PlayerController.LockCursor(true);
        }

        void Update()
        {
            if (_open == this && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        public override void OnNetworkDespawn()
        {
            if (_open == this) Close();
        }

        [Rpc(SendTo.Server)]
        void OrderRpc(int productIndex, int boxes, bool express, RpcParams rpc = default)
            => DeliveryService.Instance.ServerOrder(rpc.Receive.SenderClientId, productIndex, boxes, express);

        void OnGUI()
        {
            if (_open != this) return;
            var db = GameRoot.Instance.products;
            var ds = DeliveryService.Instance;
            int level = Economy.Instance.Level.Value;

            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 16;
            var rect = new Rect(Screen.width / 2f - 360, Screen.height / 2f - 260, 720, 520);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Get("order_title") + "   " + Loc.Get("hud_money", Economy.Instance.Money.Value.ToString("N0")));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(40))) Close();
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.Get("order_hint"));

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < db.items.Count; i++)
            {
                var p = db.items[i];
                bool locked = p.unlockLevel > level;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{Loc.Get(p.nameKey)}  ({Loc.Get("storage_" + p.storage)})", GUILayout.Width(250));
                if (locked)
                {
                    GUILayout.Label(Loc.Get("order_locked", p.unlockLevel));
                }
                else
                {
                    if (GUILayout.Button("-", GUILayout.Width(30))) _qty[i] = Mathf.Max(1, _qty[i] - 1);
                    GUILayout.Label(_qty[i].ToString(), GUILayout.Width(30));
                    if (GUILayout.Button("+", GUILayout.Width(30))) _qty[i] = Mathf.Min(ds.maxBoxesPerOrder, _qty[i] + 1);
                    if (GUILayout.Button(Loc.Get("order_normal", ds.Price(p, _qty[i], false).ToString("N0")))) OrderRpc(i, _qty[i], false);
                    if (GUILayout.Button(Loc.Get("order_express", ds.Price(p, _qty[i], true).ToString("N0")))) OrderRpc(i, _qty[i], true);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label(Loc.Get("order_pending", ds.PendingBoxes.Value));
            GUILayout.EndArea();
        }
    }
}
