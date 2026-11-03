using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    public enum StationState : byte { Idle, Cooking, Ready, Burnt }

    /// <summary>
    /// 조리 설비 공용 (오뎅바, 군고구마, 라면, 우동...). 데이터만 바꿔 끼운다.
    /// 대기 → 조리 중 → 완성(손님에게 팔림) → 오래 두면 탐(폐기).
    /// 재료는 careUses번 조리하면 떨어져서 보충해야 한다.
    /// </summary>
    public class CookStation : Interactable
    {
        public static readonly List<CookStation> All = new();

        public EquipmentData data;
        public Renderer indicator;

        public NetworkVariable<StationState> State = new(StationState.Idle);
        public NetworkVariable<int> ReadyCount = new();
        public NetworkVariable<int> Care = new();
        public NetworkVariable<float> Timer = new();

        public static List<CookStation> InZone(Zone z)
        {
            var list = new List<CookStation>();
            foreach (var s in All) if (s.data.zone == z) list.Add(s);
            return list;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) Care.Value = data.careUses;
            State.OnValueChanged += (_, v) => Paint(v);
            Paint(State.Value);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        void Paint(StationState s)
        {
            if (!indicator) return;
            var c = s switch
            {
                StationState.Cooking => new Color(1f, 0.6f, 0.1f),
                StationState.Ready => new Color(0.2f, 0.9f, 0.3f),
                StationState.Burnt => new Color(0.15f, 0.15f, 0.15f),
                _ => new Color(0.6f, 0.6f, 0.6f)
            };
            var b = new MaterialPropertyBlock();
            indicator.GetPropertyBlock(b);
            b.SetColor("_BaseColor", c);
            b.SetColor("_Color", c);
            indicator.SetPropertyBlock(b);
        }

        void Update()
        {
            if (!IsServer) return;
            switch (State.Value)
            {
                case StationState.Cooking:
                    Timer.Value -= Time.deltaTime;
                    if (Timer.Value <= 0f)
                    {
                        State.Value = StationState.Ready;
                        ReadyCount.Value = data.batchSize;
                        Timer.Value = data.burnSeconds;
                    }
                    break;
                case StationState.Ready:
                    Timer.Value -= Time.deltaTime;
                    if (Timer.Value <= 0f)
                    {
                        Economy.Instance.ServerWaste(ReadyCount.Value * data.unitCost);
                        ReadyCount.Value = 0;
                        State.Value = StationState.Burnt;
                        Announcer.Instance.Say("notice_burnt", "@" + data.nameKey);
                    }
                    break;
            }
        }

        public override string GetPrompt(PlayerController p)
        {
            string name = Loc.Get(data.nameKey);
            return State.Value switch
            {
                StationState.Idle when Care.Value <= 0 => $"{name}: {Loc.Get("prompt_refill", data.refillCost.ToString("N0"))}",
                StationState.Idle => $"{name}: {Loc.Get("prompt_cook", Care.Value)}",
                StationState.Cooking => $"{name}: {Loc.Get("prompt_cooking", Mathf.CeilToInt(Timer.Value))}",
                StationState.Ready => $"{name}: {Loc.Get("prompt_ready", ReadyCount.Value, Mathf.CeilToInt(Timer.Value))}",
                _ => $"{name}: {Loc.Get("prompt_burnt")}"
            };
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (kind != InteractKind.Primary) return;
            switch (State.Value)
            {
                case StationState.Idle:
                    if (Care.Value <= 0)
                    {
                        if (Economy.Instance.ServerTrySpend(data.refillCost)) Care.Value = data.careUses;
                        else Announcer.Instance.SayTo(p.OwnerClientId, "notice_no_money");
                        return;
                    }
                    Care.Value--;
                    Timer.Value = data.cookSeconds;
                    State.Value = StationState.Cooking;
                    break;
                case StationState.Burnt:
                    State.Value = StationState.Idle;
                    break;
            }
        }

        /// <summary>계산 시 완성품 하나를 가져간다.</summary>
        public bool ServerTakeOne()
        {
            if (State.Value != StationState.Ready || ReadyCount.Value <= 0) return false;
            ReadyCount.Value--;
            if (ReadyCount.Value <= 0) State.Value = StationState.Idle;
            return true;
        }
    }
}
