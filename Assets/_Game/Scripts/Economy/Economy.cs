using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>돈, 평판, 청결, 매장 레벨, 하루 정산.</summary>
    public class Economy : NetworkBehaviour
    {
        public static Economy Instance { get; private set; }

        public int startMoney = 300000;
        public int rent = 30000;
        public int power = 10000;
        public int revenuePerLevel = 500000;

        public NetworkVariable<int> Money = new();
        public NetworkVariable<float> Reputation = new(50f);
        public NetworkVariable<float> Cleanliness = new(100f);
        public NetworkVariable<int> TotalRevenue = new();
        public NetworkVariable<int> Level = new(1);
        public NetworkVariable<DailyReport> LastReport = new();

        // 오늘 누계 (호스트)
        DailyReport _today;
        float _cleanTick;

        void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Money.Value = startMoney;
        }

        void Update()
        {
            if (!IsServer) return;
            _cleanTick -= Time.deltaTime;
            if (_cleanTick > 0f) return;
            _cleanTick = 0.5f;
            int fullBins = 0;
            foreach (var b in TrashBin.All) if (b.IsFull) fullBins++;
            float target = Mathf.Clamp(100f - Mess.All.Count * 6f - fullBins * 10f, 0f, 100f);
            Cleanliness.Value = Mathf.MoveTowards(Cleanliness.Value, target, 4f);
        }

        // ---- 호스트 전용 ----

        public bool ServerTrySpend(int amount)
        {
            if (Money.Value < amount) return false;
            Money.Value -= amount;
            return true;
        }

        public void ServerSale(int price, int cost)
        {
            Money.Value += price;
            TotalRevenue.Value += price;
            Level.Value = Mathf.Clamp(1 + TotalRevenue.Value / revenuePerLevel, 1, 10);
            _today.revenue += price;
            _today.cogs += cost;
        }

        public void ServerWaste(int cost) => _today.waste += cost;

        public void ServerEventMoney(int delta)
        {
            Money.Value += delta;
            _today.events += delta;
        }

        public void ServerRep(float delta) => Reputation.Value = Mathf.Clamp(Reputation.Value + delta, 0f, 100f);
        public void ServerCustomerDone(bool happy)
        {
            if (happy) _today.customers++;
            else _today.angry++;
        }
        public void ServerNewFace() => _today.newFaces++;

        public DailyReport ServerCloseDay(int day)
        {
            foreach (var s in ShelfSlot.All) _today.waste += s.ServerExpire(day);
            foreach (var st in CookStation.All) _today.equipment += st.data.dailyCost;
            _today.fixedCost = rent + power;
            _today.day = day;
            _today.net = _today.revenue - _today.cogs - _today.waste - _today.equipment - _today.fixedCost + _today.events;

            Money.Value -= _today.equipment + _today.fixedCost;
            if (Cleanliness.Value < 50f) ServerRep(-3f);

            var r = _today;
            LastReport.Value = r;
            _today = default;
            return r;
        }

        public void ServerLoad(int money, float rep, int totalRevenue)
        {
            Money.Value = money;
            Reputation.Value = rep;
            TotalRevenue.Value = totalRevenue;
            Level.Value = Mathf.Clamp(1 + totalRevenue / revenuePerLevel, 1, 10);
        }
    }
}
