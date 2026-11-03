using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RestStop
{
    /// <summary>호스트 컴퓨터에만 저장한다. 마감 정산 직후 자동 저장, 다음 날 아침부터 이어서.</summary>
    public static class SaveSystem
    {
        [Serializable] class SlotSave { public string key; public string product; public int count; public int stockedDay; }
        [Serializable] class OrderSave { public string product; public int boxes; }
        [Serializable] class BoxSave { public string product; public int remaining; public Vector3 pos; public float yaw; }
        [Serializable]
        class SaveData
        {
            public int version = 1;
            public int day;
            public int money;
            public float reputation;
            public int totalRevenue;
            public int daysWithoutEpic;
            public List<string> seen = new();
            public List<SlotSave> slots = new();
            public List<OrderSave> orders = new();
            public List<BoxSave> boxes = new();
        }

        static string PathFor(int slot) => Path.Combine(Application.persistentDataPath, $"reststop_slot{slot}.json");
        public static int Slot = 1;

        public static bool Exists => File.Exists(PathFor(Slot));
        public static void Delete() { if (Exists) File.Delete(PathFor(Slot)); }

        public static void Save()
        {
            var root = GameRoot.Instance;
            var d = new SaveData
            {
                day = DayCycle.Instance.Day.Value + 1,
                money = Economy.Instance.Money.Value,
                reputation = Economy.Instance.Reputation.Value,
                totalRevenue = Economy.Instance.TotalRevenue.Value,
                daysWithoutEpic = CodexState.Instance.DaysWithoutEpic.Value
            };
            foreach (var i in CodexState.Instance.Seen)
            {
                var c = root.customers.Get(i);
                if (c != null) d.seen.Add(c.id);
            }
            foreach (var s in ShelfSlot.All)
                if (s.Product != null && s.Count.Value > 0)
                    d.slots.Add(new SlotSave { key = s.SlotKey, product = s.Product.id, count = s.Count.Value, stockedDay = s.StockedDay.Value });
            foreach (var o in DeliveryService.Instance.Pending)
            {
                var p = root.products.Get(o.product);
                if (p != null) d.orders.Add(new OrderSave { product = p.id, boxes = o.boxes });
            }
            foreach (var b in UnityEngine.Object.FindObjectsByType<ProductBox>(FindObjectsSortMode.None))
            {
                if (!b.IsSpawned || b.IsTrash || b.Product == null) continue;
                d.boxes.Add(new BoxSave { product = b.Product.id, remaining = b.Remaining.Value, pos = b.RestPos.Value, yaw = b.transform.eulerAngles.y });
            }

            try { File.WriteAllText(PathFor(Slot), JsonUtility.ToJson(d, true)); }
            catch (Exception e) { Debug.LogError($"Save failed: {e.Message}"); }
        }

        public static void Load()
        {
            if (!Exists) return;
            SaveData d;
            try { d = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFor(Slot))); }
            catch (Exception e) { Debug.LogError($"Load failed: {e.Message}"); return; }
            if (d == null) return;

            var root = GameRoot.Instance;
            DayCycle.Instance.Day.Value = Mathf.Max(1, d.day);
            Economy.Instance.ServerLoad(d.money, d.reputation, d.totalRevenue);
            CodexState.Instance.DaysWithoutEpic.Value = d.daysWithoutEpic;
            foreach (var id in d.seen)
            {
                int idx = root.customers.items.FindIndex(c => c != null && c.id == id);
                if (idx >= 0) CodexState.Instance.ServerRegister(idx);
            }

            var byKey = new Dictionary<string, ShelfSlot>();
            foreach (var s in ShelfSlot.All) byKey[s.SlotKey] = s;
            foreach (var s in d.slots)
            {
                int p = root.products.IndexOfId(s.product);
                if (p >= 0 && byKey.TryGetValue(s.key, out var slot)) slot.ServerSet(p, s.count, s.stockedDay);
            }

            foreach (var o in d.orders)
            {
                int p = root.products.IndexOfId(o.product);
                if (p >= 0) DeliveryService.Instance.Pending.Add(new PendingOrder { product = p, boxes = o.boxes, arriveDay = d.day, arriveHour = -1f });
            }

            foreach (var b in d.boxes)
            {
                int p = root.products.IndexOfId(b.product);
                if (p < 0) continue;
                var box = UnityEngine.Object.Instantiate(root.boxPrefab, b.pos, Quaternion.Euler(0f, b.yaw, 0f));
                box.NetworkObject.Spawn(true);
                box.ServerInit(p, b.remaining);
            }
        }
    }
}
