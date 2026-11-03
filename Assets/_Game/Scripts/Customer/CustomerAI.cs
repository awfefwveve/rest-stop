using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace RestStop
{
    public enum CustomerState : byte { Shopping, ToCounter, Queued, Eating, Leaving }

    /// <summary>
    /// 손님 AI. 판단과 이동은 호스트만 하고, 위치는 NetworkTransform으로 나머지에게 간다.
    /// 편의점 손님: 진열대에서 상품 집기 → 계산대 줄 → 계산 → 퇴장
    /// 식당가 손님: 식당 계산대 줄 → 조리된 음식 받기 → 좌석에서 먹기 → 퇴장
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CustomerAI : Interactable
    {
        public static readonly List<CustomerAI> All = new();
        static readonly HashSet<int> _seatsTaken = new();

        public float dwellSeconds = 0.8f;
        public float eatSeconds = 10f;

        public NetworkVariable<int> DataIndex = new(-1);
        public NetworkVariable<CustomerState> State = new(CustomerState.Shopping);
        public NetworkVariable<FixedString64Bytes> Bubble = new();     // 말풍선 Loc 키
        public NetworkVariable<float> Patience01 = new(1f);
        public NetworkVariable<bool> EventWaiting = new();              // 이벤트로 플레이어 행동을 기다리는 중

        // ---- 호스트 전용 상태 ----
        NavMeshAgent _agent;
        CustomerData _data;
        readonly List<int> _cart = new();
        readonly List<CookStation> _stationOrders = new();
        int _wanted, _retries;
        ShelfSlot _target;
        CheckoutCounter _counter;
        float _dwell, _patience, _eat, _bubbleTimer;
        int _seat = -1;
        int _queueSlot = -1;
        EventData _pendingEvent;
        bool _eventFired;

        public CustomerData Data => GameRoot.Instance.customers.Get(DataIndex.Value);
        public int RemainingToScan => _cart.Count + _stationOrders.Count;
        public bool IsBlockingCounter => EventWaiting.Value;

        void Awake() => _agent = GetComponent<NavMeshAgent>();

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (!IsServer) _agent.enabled = false;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer)
            {
                FreeSeat();
                if (_counter != null) _counter.ServerLeave(this);
                EventRunner.Instance?.ServerForget(this);
            }
        }

        // ---- 상호작용 (이벤트 대처) ----

        public override string GetPrompt(PlayerController p)
        {
            var d = Data;
            if (d == null) return "";
            string name = Loc.Get(d.nameKey);
            return EventWaiting.Value ? $"{name}: {Loc.Get("prompt_event_respond")}" : name;
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
            => EventRunner.Instance.ServerHandleInteract(this, kind);

        // ---- 호스트 전용 ----

        public void ServerInit(int dataIndex)
        {
            DataIndex.Value = dataIndex;
            _data = GameRoot.Instance.customers.Get(dataIndex);
            _patience = _data.patience;
            _wanted = Random.Range(_data.itemsMin, _data.itemsMax + 1);
            Say(_data.idleLineKey, 3f);

            // 식당가로 갈지
            var foodStations = CookStation.InZone(Zone.FoodCourt);
            var foodCounter = CheckoutCounter.Find(Zone.FoodCourt);
            if (foodStations.Count > 0 && foodCounter != null && Random.value < _data.foodCourtChance)
            {
                int n = Random.Range(1, 3);
                for (int i = 0; i < n; i++) _stationOrders.Add(foodStations[Random.Range(0, foodStations.Count)]);
                _wanted = 0;
                _counter = foodCounter;
            }
            else
            {
                var shopStations = CookStation.InZone(Zone.Shop);
                if (shopStations.Count > 0 && Random.value < _data.equipmentOrderChance)
                    _stationOrders.Add(shopStations[Random.Range(0, shopStations.Count)]);
                _counter = CheckoutCounter.Find(Zone.Shop);
            }

            // 이벤트 예약
            if (_data.events != null && _data.events.Length > 0 && Random.value < _data.eventChance)
                _pendingEvent = _data.events[Random.Range(0, _data.events.Length)];

            State.Value = CustomerState.Shopping;
        }

        void Update()
        {
            if (!IsServer || _data == null || !_agent.isOnNavMesh) return;

            if (_bubbleTimer > 0f)
            {
                _bubbleTimer -= Time.deltaTime;
                if (_bubbleTimer <= 0f && !EventWaiting.Value) Bubble.Value = default;
            }

            switch (State.Value)
            {
                case CustomerState.Shopping: TickShopping(); break;
                case CustomerState.ToCounter:
                case CustomerState.Queued: TickQueue(); break;
                case CustomerState.Eating: TickEating(); break;
                case CustomerState.Leaving:
                    if (Arrived(0.8f)) NetworkObject.Despawn(true);
                    break;
            }
        }

        bool Arrived(float tol = 0.4f)
            => !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(tol, _agent.stoppingDistance);

        void TickShopping()
        {
            if (_wanted <= 0) { FinishShopping(); return; }

            if (_target == null || !_target.HasStock)
            {
                _target = PickSlot();
                if (_target == null)
                {
                    // 원하는 게 품절
                    Economy.Instance.ServerRep(-0.5f);
                    Say("line_out_of_stock", 3f);
                    _wanted = 0;
                    return;
                }
                _agent.SetDestination(_target.AccessPoint);
                _dwell = dwellSeconds;
                return;
            }

            if (!Arrived()) return;
            _dwell -= Time.deltaTime;
            if (_dwell > 0f) return;

            int idx = _target.ServerTake();
            _target = null;
            if (idx < 0) { if (++_retries > 3) _wanted = 0; return; }
            _cart.Add(idx);
            _wanted--;

            if (Random.value < _data.messiness) Mess.ServerSpawn(transform.position + transform.forward * 0.5f);

            if (_pendingEvent != null && !_eventFired && _pendingEvent.type == GameEventType.SpawnMess)
                FireEvent();
        }

        ShelfSlot PickSlot()
        {
            ShelfSlot best = null;
            float bestScore = float.MaxValue;
            foreach (var s in ShelfSlot.All)
            {
                if (!s.HasStock) continue;
                var p = s.Product;
                if (_data.likes != null && _data.likes.Length > 0 && System.Array.IndexOf(_data.likes, p.category) < 0) continue;
                float score = Vector3.Distance(transform.position, s.AccessPoint) + Random.Range(0f, 6f);
                if (score < bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        void FinishShopping()
        {
            if (RemainingToScan == 0 || _counter == null)
            {
                ServerLeave(false);
                return;
            }
            _counter.ServerJoin(this);
            _queueSlot = -1;
            State.Value = CustomerState.ToCounter;
        }

        void TickQueue()
        {
            if (_counter == null) { ServerLeave(false); return; }
            int i = _counter.IndexOf(this);
            if (i < 0) { ServerLeave(false); return; }
            if (i != _queueSlot) { _queueSlot = i; _agent.SetDestination(_counter.QueuePoint(i)); return; }

            if (State.Value == CustomerState.ToCounter && Arrived(0.6f)) State.Value = CustomerState.Queued;

            // 맨 앞에 도착하면 계산대 이벤트 발동
            if (i == 0 && State.Value == CustomerState.Queued && _pendingEvent != null && !_eventFired
                && _pendingEvent.type != GameEventType.SpawnMess)
                FireEvent();

            if (EventWaiting.Value) return;   // 이벤트 중엔 인내심 그대로

            _patience -= Time.deltaTime;
            Patience01.Value = Mathf.Clamp01(_patience / _data.patience);
            if (_patience < _data.patience * 0.3f && _bubbleTimer <= 0f) Say("line_hurry", 2.5f);
            if (_patience <= 0f) Storm();
        }

        void FireEvent()
        {
            _eventFired = true;
            if (EventRunner.Instance.ServerTryStart(this, _pendingEvent)) return;
            _pendingEvent = null;
        }

        /// <summary>참다 못해 장바구니를 내던지고 나간다.</summary>
        void Storm()
        {
            int lost = 0;
            foreach (var idx in _cart) lost += GameRoot.Instance.products.Get(idx).cost;
            _cart.Clear();
            _stationOrders.Clear();
            if (lost > 0)
            {
                Economy.Instance.ServerWaste(lost);
                Mess.ServerSpawn(transform.position);
            }
            Economy.Instance.ServerRep(-3f);
            Say("line_angry", 3f);
            ServerLeave(true);
        }

        void TickEating()
        {
            if (!Arrived()) return;
            _eat -= Time.deltaTime;
            if (_eat > 0f) return;
            if (Random.value < 0.5f) Mess.ServerSpawn(transform.position + transform.forward * 0.4f);
            FreeSeat();
            ServerLeave(false);
        }

        void FreeSeat()
        {
            if (_seat >= 0) _seatsTaken.Remove(_seat);
            _seat = -1;
        }

        public void Say(string key, float seconds)
        {
            if (string.IsNullOrEmpty(key)) return;
            Bubble.Value = key;
            _bubbleTimer = seconds;
        }

        /// <summary>계산대가 한 번 찍을 때 호출. true = 다 끝남.</summary>
        public ScanResult ServerScanOne()
        {
            if (_cart.Count > 0)
            {
                var p = GameRoot.Instance.products.Get(_cart[^1]);
                _cart.RemoveAt(_cart.Count - 1);
                Economy.Instance.ServerSale(p.price, p.cost);
                return RemainingToScan == 0 ? ScanResult.Done : ScanResult.Scanned;
            }
            if (_stationOrders.Count > 0)
            {
                var st = _stationOrders[0];
                if (st == null || !st.IsSpawned) { _stationOrders.RemoveAt(0); return RemainingToScan == 0 ? ScanResult.Done : ScanResult.Scanned; }
                if (!st.ServerTakeOne()) return ScanResult.NeedCook;
                _stationOrders.RemoveAt(0);
                Economy.Instance.ServerSale(st.data.price, st.data.unitCost);
                return RemainingToScan == 0 ? ScanResult.Done : ScanResult.Scanned;
            }
            return ScanResult.Done;
        }

        public CookStation WaitingStation => _stationOrders.Count > 0 ? _stationOrders[0] : null;

        public void ServerPaid()
        {
            Economy.Instance.ServerCustomerDone(true);
            Economy.Instance.ServerRep(_patience > _data.patience * 0.5f ? 0.6f : 0.2f);
            Say("line_thanks", 2f);

            if (_counter != null && _counter.zone == Zone.FoodCourt)
            {
                var seats = LevelLayout.Instance.seats;
                for (int i = 0; i < seats.Length; i++)
                {
                    if (_seatsTaken.Contains(i)) continue;
                    _seatsTaken.Add(i);
                    _seat = i;
                    _counter = null;
                    _eat = eatSeconds;
                    _agent.SetDestination(seats[i].position);
                    State.Value = CustomerState.Eating;
                    return;
                }
            }
            else if (Random.value < 0.4f)
            {
                TrashBin.ServerNearest(transform.position)?.ServerAdd(1);
            }
            _counter = null;
            ServerLeave(false);
        }

        public void ServerLeave(bool angry)
        {
            if (State.Value == CustomerState.Leaving) return;
            if (angry) Economy.Instance.ServerCustomerDone(false);
            if (_counter != null) { _counter.ServerLeave(this); _counter = null; }
            EventRunner.Instance?.ServerForget(this);
            EventWaiting.Value = false;
            FreeSeat();
            State.Value = CustomerState.Leaving;
            if (_agent.isOnNavMesh) _agent.SetDestination(LevelLayout.Instance.exit.position);
        }

        public void ServerSetEventWaiting(bool waiting) => EventWaiting.Value = waiting;

        public static void ResetSeats() => _seatsTaken.Clear();
    }

    public enum ScanResult { Scanned, NeedCook, Done }
}
