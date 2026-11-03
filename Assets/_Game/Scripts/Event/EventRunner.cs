using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 손님 이벤트 실행기. 하루 상한이 있어서 영업 루프가 이벤트에 묻히지 않는다.
    /// - SpawnMess: 주변을 어지럽힘 → 제한 시간 안에 전부 청소하면 성공
    /// - BlockUntilPoked: 계산대에서 멈춤(잠듦 등) → 손님을 누르면 성공
    /// - StrangeCoin: 이상한 돈 → 받기(좌클릭)는 도박, 거절(우클릭)은 손님 퇴장
    /// </summary>
    public class EventRunner : NetworkBehaviour
    {
        public static EventRunner Instance { get; private set; }

        public int maxPerDay = 4;
        [Range(0, 1)] public float coinWinChance = 0.6f;

        class Active
        {
            public EventData data;
            public CustomerAI who;
            public readonly List<Mess> messes = new();
            public float timer;
        }

        readonly List<Active> _active = new();
        int _today;

        void Awake() => Instance = this;

        public bool ServerTryStart(CustomerAI who, EventData ev)
        {
            if (ev == null || _today >= maxPerDay) return false;
            _today++;
            var a = new Active { data = ev, who = who, timer = ev.timeLimit };
            switch (ev.type)
            {
                case GameEventType.SpawnMess:
                    for (int i = 0; i < ev.messCount; i++)
                    {
                        var off = Random.insideUnitSphere * 2f;
                        off.y = 0f;
                        a.messes.Add(Mess.ServerSpawn(who.transform.position + off));
                    }
                    break;
                default:
                    who.ServerSetEventWaiting(true);
                    break;
            }
            who.Say(ev.lineKey, ev.timeLimit);
            _active.Add(a);
            Announcer.Instance.Say("notice_event", "@" + ev.titleKey);
            return true;
        }

        void Update()
        {
            if (!IsServer) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                a.timer -= Time.deltaTime;
                if (a.data.type == GameEventType.SpawnMess)
                {
                    bool clean = a.messes.TrueForAll(m => m == null || !m.IsSpawned);
                    if (clean) { Resolve(a, true); continue; }
                }
                if (a.timer <= 0f)
                {
                    if (a.data.type == GameEventType.StrangeCoin) Refuse(a);
                    else Resolve(a, false);
                }
            }
        }

        public void ServerHandleInteract(CustomerAI who, InteractKind kind)
        {
            var a = _active.Find(x => x.who == who);
            if (a == null) return;
            switch (a.data.type)
            {
                case GameEventType.BlockUntilPoked:
                    Resolve(a, true);
                    break;
                case GameEventType.StrangeCoin:
                    if (kind == InteractKind.Secondary) { Refuse(a); break; }
                    Resolve(a, Random.value < coinWinChance);
                    break;
            }
        }

        void Refuse(Active a)
        {
            _active.Remove(a);
            Economy.Instance.ServerRep(-1f);
            if (a.who != null && a.who.IsSpawned)
            {
                a.who.ServerSetEventWaiting(false);
                a.who.Say("line_refused", 2f);
                a.who.ServerLeave(false);
            }
        }

        void Resolve(Active a, bool success)
        {
            _active.Remove(a);
            var e = Economy.Instance;
            int money = success ? a.data.successMoney : a.data.failMoney;
            if (money != 0) e.ServerEventMoney(money);
            e.ServerRep(success ? a.data.successRep : a.data.failRep);
            if (a.who != null && a.who.IsSpawned)
            {
                a.who.ServerSetEventWaiting(false);
                a.who.Say(success ? "line_event_ok" : "line_event_fail", 2.5f);
            }
            Announcer.Instance.Say(success ? "notice_event_ok" : "notice_event_fail", "@" + a.data.titleKey, money.ToString("N0"));
        }

        public void ServerForget(CustomerAI who) => _active.RemoveAll(x => x.who == who && x.data.type != GameEventType.SpawnMess);

        public void ServerEndDay()
        {
            _active.Clear();
            _today = 0;
        }
    }
}
