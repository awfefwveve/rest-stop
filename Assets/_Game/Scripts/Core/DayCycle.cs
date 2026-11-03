using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>오픈 준비 → 영업 → 마감 → 정산. 하루 = 실제 12분 (07시~23시).</summary>
    public class DayCycle : NetworkBehaviour
    {
        public static DayCycle Instance { get; private set; }

        public float dayLengthSeconds = 720f;
        public float openHour = 7f;
        public float closeHour = 23f;
        public float closingTimeout = 25f;

        public NetworkVariable<DayPhase> Phase = new(DayPhase.Prep);
        public NetworkVariable<int> Day = new(1);
        public NetworkVariable<float> Hour = new(7f);

        float _closingTimer;

        void Awake() => Instance = this;

        void Update()
        {
            if (!IsServer) return;
            switch (Phase.Value)
            {
                case DayPhase.Open:
                    Hour.Value += Time.deltaTime * (closeHour - openHour) / dayLengthSeconds;
                    if (Hour.Value >= closeHour)
                    {
                        Hour.Value = closeHour;
                        Phase.Value = DayPhase.Closing;
                        _closingTimer = 0f;
                        CustomerSpawner.Instance.ServerSendAllHome();
                        Announcer.Instance.Say("notice_closing");
                    }
                    break;
                case DayPhase.Closing:
                    _closingTimer += Time.deltaTime;
                    if (CustomerAI.All.Count == 0 || _closingTimer > closingTimeout)
                    {
                        CustomerSpawner.Instance.ServerDespawnAll();
                        Settle();
                    }
                    break;
            }
        }

        void Settle()
        {
            CustomerSpawner.Instance.ServerEndDay();
            EventRunner.Instance.ServerEndDay();
            Economy.Instance.ServerCloseDay(Day.Value);
            Phase.Value = DayPhase.Closed;
            SaveSystem.Save();
        }

        // ---- 호스트 전용 (HUD 버튼) ----

        public void ServerBeginPrep()
        {
            Phase.Value = DayPhase.Prep;
            Hour.Value = openHour;
            DeliveryService.Instance.ServerDeliverMorning();
            CustomerSpawner.Instance.ServerBeginDay(openHour);
        }

        public void ServerOpen()
        {
            if (Phase.Value != DayPhase.Prep) return;
            Phase.Value = DayPhase.Open;
            Announcer.Instance.Say("notice_open");
        }

        public void ServerNextDay()
        {
            if (Phase.Value != DayPhase.Closed) return;
            Day.Value++;
            ServerBeginPrep();
        }

        public static string Clock(float hour)
        {
            int h = Mathf.FloorToInt(hour);
            int m = Mathf.FloorToInt((hour - h) * 60f);
            return $"{h:00}:{m:00}";
        }
    }
}
