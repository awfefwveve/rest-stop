using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 계산대 (편의점 / 식당가 공용). 손님 줄을 관리하고, 누를 때마다 물건 하나를 찍는다.
    /// queueOrigin의 forward 방향으로 줄이 늘어선다.
    /// </summary>
    public class CheckoutCounter : Interactable
    {
        public static readonly List<CheckoutCounter> All = new();

        public Zone zone = Zone.Shop;
        public Transform queueOrigin;
        public float queueGap = 0.8f;

        public NetworkVariable<int> QueueCount = new();
        public NetworkVariable<int> FrontRemaining = new();
        public NetworkVariable<FixedString64Bytes> Status = new();   // Loc 키
        public NetworkVariable<FixedString64Bytes> StatusArg = new();

        readonly List<CustomerAI> _queue = new();

        public static CheckoutCounter Find(Zone z)
        {
            foreach (var c in All) if (c.zone == z) return c;
            return null;
        }

        public override void OnNetworkSpawn() => All.Add(this);
        public override void OnNetworkDespawn() => All.Remove(this);

        public Vector3 QueuePoint(int i) => queueOrigin.position + queueOrigin.forward * (i * queueGap);
        public int IndexOf(CustomerAI ai) => _queue.IndexOf(ai);

        public override string GetPrompt(PlayerController p)
        {
            if (QueueCount.Value == 0) return Loc.Get("prompt_no_customer");
            var s = Status.Value.ToString();
            if (!string.IsNullOrEmpty(s)) return Loc.Get(s, Loc.Get(StatusArg.Value.ToString()));
            return Loc.Get("prompt_scan", FrontRemaining.Value);
        }

        void Update()
        {
            if (!IsServer) return;
            _queue.RemoveAll(c => c == null || !c.IsSpawned);
            if (QueueCount.Value != _queue.Count) QueueCount.Value = _queue.Count;
            int rem = _queue.Count > 0 ? _queue[0].RemainingToScan : 0;
            if (FrontRemaining.Value != rem) FrontRemaining.Value = rem;
            if (_queue.Count == 0 && Status.Value.Length > 0) SetStatus("", "");
        }

        void SetStatus(string key, string arg)
        {
            Status.Value = key;
            StatusArg.Value = arg;
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (kind != InteractKind.Primary || _queue.Count == 0) return;
            var front = _queue[0];
            if (front.State.Value != CustomerState.Queued) { SetStatus("status_wait_customer", ""); return; }
            if (front.IsBlockingCounter) { SetStatus("status_event", ""); return; }

            switch (front.ServerScanOne())
            {
                case ScanResult.Scanned:
                    SetStatus("", "");
                    break;
                case ScanResult.NeedCook:
                    var st = front.WaitingStation;
                    SetStatus("status_need_cook", st != null ? st.data.nameKey : "");
                    break;
                case ScanResult.Done:
                    SetStatus("", "");
                    _queue.RemoveAt(0);
                    front.ServerPaid();
                    break;
            }
        }

        // ---- 호스트 전용 ----
        public void ServerJoin(CustomerAI ai) { if (!_queue.Contains(ai)) _queue.Add(ai); }
        public void ServerLeave(CustomerAI ai) => _queue.Remove(ai);

        void OnDrawGizmos()
        {
            if (!queueOrigin) return;
            Gizmos.color = Color.green;
            for (int i = 0; i < 6; i++) Gizmos.DrawWireSphere(queueOrigin.position + queueOrigin.forward * (i * queueGap), 0.2f);
        }
    }
}
