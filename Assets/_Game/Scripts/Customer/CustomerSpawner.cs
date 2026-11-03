using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 손님 입장. 평소엔 띄엄띄엄(트리클), 관광버스가 오면 한꺼번에(웨이브).
    /// 인원이 많을수록 손님도 많아진다.
    /// </summary>
    public class CustomerSpawner : NetworkBehaviour
    {
        public static CustomerSpawner Instance { get; private set; }

        public int maxAlive = 40;
        public float tricklePerHour = 5f;       // 1인 기준, 게임 시간 1시간당
        public float firstBusHour = 8.5f;
        public float busEveryHours = 2.5f;
        public int busBase = 6;
        public int busPerPlayer = 5;
        public float spawnGap = 0.5f;
        public int discoveryBonus = 3000;        // 도감 신규 보너스 (희귀도마다 배)

        readonly Queue<Transform> _queue = new();
        float _lastHour, _trickle, _nextBus, _gap;
        bool _epicToday;

        void Awake() => Instance = this;

        int Players => Mathf.Clamp(NetworkManager.ConnectedClientsIds.Count, 1, GameRoot.MaxPlayers);

        void Update()
        {
            if (!IsServer || DayCycle.Instance.Phase.Value != DayPhase.Open) return;
            var layout = LevelLayout.Instance;
            float hour = DayCycle.Instance.Hour.Value;
            float dh = Mathf.Max(0f, hour - _lastHour);
            _lastHour = hour;

            // 트리클: 인원 1명 1.0배, 4명 2.2배
            _trickle += dh * tricklePerHour * (0.6f + 0.4f * Players);
            while (_trickle >= 1f) { _trickle -= 1f; _queue.Enqueue(layout.entrance); }

            // 버스 웨이브
            if (hour >= _nextBus)
            {
                _nextBus += busEveryHours;
                int n = busBase + busPerPlayer * Players;
                for (int i = 0; i < n; i++) _queue.Enqueue(layout.busStop);
                Announcer.Instance.Say("notice_bus", n.ToString());
            }

            _gap -= Time.deltaTime;
            if (_gap <= 0f && _queue.Count > 0 && CustomerAI.All.Count < maxAlive)
            {
                _gap = spawnGap;
                SpawnOne(_queue.Dequeue());
            }
        }

        void SpawnOne(Transform at)
        {
            var ids = new HashSet<string>();
            foreach (var st in CookStation.All) ids.Add(st.data.id);

            bool forceEpic = !_epicToday && CodexState.Instance.PityReady;
            var db = GameRoot.Instance.customers;
            var data = CustomerGacha.Roll(db, Economy.Instance.Reputation.Value, DayCycle.Instance.Hour.Value, forceEpic, ids);
            if (data == null) return;

            var jitter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            var no = Instantiate(data.prefab, at.position + jitter, at.rotation);
            no.Spawn(true);
            var ai = no.GetComponent<CustomerAI>();
            int index = db.IndexOf(data);
            ai.ServerInit(index);

            if (data.rarity >= Rarity.Epic) _epicToday = true;
            if (data.rarity >= Rarity.Rare) Announcer.Instance.Rarity(data.rarity, data.nameKey);
            if (CodexState.Instance.ServerRegister(index))
            {
                int bonus = discoveryBonus * ((int)data.rarity + 1);
                Economy.Instance.ServerEventMoney(bonus);
                Economy.Instance.ServerNewFace();
                Announcer.Instance.Say("notice_new_face", "@" + data.nameKey, bonus.ToString("N0"));
            }
        }

        // ---- 호스트 전용 ----

        public void ServerBeginDay(float openHour)
        {
            _queue.Clear();
            _lastHour = openHour;
            _trickle = 0f;
            _nextBus = firstBusHour;
            _epicToday = false;
        }

        public void ServerEndDay()
        {
            var codex = CodexState.Instance;
            codex.DaysWithoutEpic.Value = _epicToday ? 0 : codex.DaysWithoutEpic.Value + 1;
            _queue.Clear();
            CustomerAI.ResetSeats();
        }

        public void ServerSendAllHome()
        {
            _queue.Clear();
            foreach (var c in CustomerAI.All.ToArray()) c.ServerLeave(false);
        }

        public void ServerDespawnAll()
        {
            foreach (var c in CustomerAI.All.ToArray())
                if (c != null && c.IsSpawned) c.NetworkObject.Despawn(true);
        }
    }
}
