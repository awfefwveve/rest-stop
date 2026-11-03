using Unity.Netcode;

namespace RestStop
{
    /// <summary>손님 도감 + 천장(7일 동안 영웅 이상이 안 나오면 다음 날 1명 확정).</summary>
    public class CodexState : NetworkBehaviour
    {
        public static CodexState Instance { get; private set; }

        public NetworkList<int> Seen;
        public NetworkVariable<int> DaysWithoutEpic = new();

        public int pityDays = 7;

        void Awake()
        {
            Instance = this;
            Seen = new NetworkList<int>();
        }

        public bool PityReady => DaysWithoutEpic.Value >= pityDays;

        public bool ServerRegister(int customerIndex)
        {
            if (Seen.Contains(customerIndex)) return false;
            Seen.Add(customerIndex);
            return true;
        }
    }
}
