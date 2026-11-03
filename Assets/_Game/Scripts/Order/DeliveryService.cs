using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    [Serializable]
    public struct PendingOrder
    {
        public int product;
        public int boxes;
        public int arriveDay;
        public float arriveHour;   // < 0 이면 아침 입고
    }

    /// <summary>주문 결제와 트럭 입고. 일반 주문은 다음 날 아침, 급배송은 2시간 뒤.</summary>
    public class DeliveryService : NetworkBehaviour
    {
        public static DeliveryService Instance { get; private set; }

        public float expressHours = 2f;
        public float expressMultiplier = 1.3f;
        public int maxBoxesPerOrder = 10;

        public readonly List<PendingOrder> Pending = new();
        public NetworkVariable<int> PendingBoxes = new();

        int _spot;

        void Awake() => Instance = this;

        public int Price(ProductData p, int boxes, bool express)
            => Mathf.RoundToInt(p.cost * p.unitsPerBox * boxes * (express ? expressMultiplier : 1f));

        void Update()
        {
            if (!IsServer || DayCycle.Instance == null) return;
            if (DayCycle.Instance.Phase.Value != DayPhase.Open && DayCycle.Instance.Phase.Value != DayPhase.Prep) return;
            int day = DayCycle.Instance.Day.Value;
            float hour = DayCycle.Instance.Hour.Value;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var o = Pending[i];
                if (o.arriveHour >= 0f && o.arriveDay <= day && hour >= o.arriveHour)
                {
                    Pending.RemoveAt(i);
                    SpawnBoxes(o);
                    Announcer.Instance.Say("notice_express_arrived", "@" + GameRoot.Instance.products.Get(o.product).nameKey);
                }
            }
            RefreshCount();
        }

        // ---- 호스트 전용 ----

        public void ServerOrder(ulong clientId, int productIndex, int boxes, bool express)
        {
            var p = GameRoot.Instance.products.Get(productIndex);
            if (p == null || boxes <= 0 || boxes > maxBoxesPerOrder) return;
            if (p.unlockLevel > Economy.Instance.Level.Value) return;
            if (DayCycle.Instance.Phase.Value == DayPhase.Closed || DayCycle.Instance.Phase.Value == DayPhase.Closing) express = false;

            int price = Price(p, boxes, express);
            if (!Economy.Instance.ServerTrySpend(price))
            {
                Announcer.Instance.SayTo(clientId, "notice_no_money");
                return;
            }

            int day = DayCycle.Instance.Day.Value;
            Pending.Add(new PendingOrder
            {
                product = productIndex,
                boxes = boxes,
                arriveDay = express ? day : day + 1,
                arriveHour = express ? Mathf.Max(DayCycle.Instance.Hour.Value, DayCycle.Instance.openHour) + expressHours : -1f
            });
            RefreshCount();
            Announcer.Instance.Say(express ? "notice_ordered_express" : "notice_ordered", "@" + p.nameKey, boxes.ToString());
        }

        /// <summary>새 게임: 1레벨 상품마다 2박스씩 무료로 첫날 아침에 들어온다.</summary>
        public void ServerGrantStarter()
        {
            var db = GameRoot.Instance.products;
            for (int i = 0; i < db.items.Count; i++)
                if (db.items[i].unlockLevel <= 1)
                    Pending.Add(new PendingOrder { product = i, boxes = 2, arriveDay = 1, arriveHour = -1f });
        }

        public void ServerDeliverMorning()
        {
            int day = DayCycle.Instance.Day.Value;
            bool any = false;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var o = Pending[i];
                if (o.arriveHour < 0f && o.arriveDay <= day)
                {
                    Pending.RemoveAt(i);
                    SpawnBoxes(o);
                    any = true;
                }
            }
            RefreshCount();
            if (any) Announcer.Instance.Say("notice_truck_arrived");
        }

        void SpawnBoxes(PendingOrder o)
        {
            var p = GameRoot.Instance.products.Get(o.product);
            if (p == null) return;
            for (int b = 0; b < o.boxes; b++)
            {
                var pos = LevelLayout.Instance.RandomDeliverySpot(_spot++ % 48);
                var box = Instantiate(GameRoot.Instance.boxPrefab, pos, LevelLayout.Instance.deliveryArea.rotation);
                box.NetworkObject.Spawn(true);
                box.ServerInit(o.product, p.unitsPerBox);
            }
        }

        void RefreshCount()
        {
            int n = 0;
            foreach (var o in Pending) n += o.boxes;
            if (PendingBoxes.Value != n) PendingBoxes.Value = n;
        }
    }
}
