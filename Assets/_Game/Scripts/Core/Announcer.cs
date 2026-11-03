using Unity.Netcode;

namespace RestStop
{
    /// <summary>
    /// 모두에게 알림을 띄운다. 문장은 각자 언어로 번역되도록 키로 보낸다.
    /// 인자 앞에 @를 붙이면 그 인자도 번역 키로 취급한다.
    /// </summary>
    public class Announcer : NetworkBehaviour
    {
        public static Announcer Instance { get; private set; }
        void Awake() => Instance = this;

        public void Say(string key, string a0 = "", string a1 = "") => NoticeRpc(key, a0, a1);

        public void SayTo(ulong clientId, string key, string a0 = "", string a1 = "")
            => NoticeToRpc(key, a0, a1, RpcTarget.Single(clientId, RpcTargetUse.Temp));

        public void Rarity(Rarity rarity, string nameKey) => RarityRpc((int)rarity, nameKey);

        [Rpc(SendTo.ClientsAndHost)]
        void NoticeRpc(string key, string a0, string a1) => HUD.Notify(Format(key, a0, a1));

        [Rpc(SendTo.SpecifiedInParams)]
        void NoticeToRpc(string key, string a0, string a1, RpcParams rpc) => HUD.Notify(Format(key, a0, a1));

        [Rpc(SendTo.ClientsAndHost)]
        void RarityRpc(int rarity, string nameKey) => HUD.Banner((Rarity)rarity, Loc.Get(nameKey));

        static string Format(string key, string a0, string a1)
            => Loc.Get(key, Arg(a0), Arg(a1));

        static string Arg(string a) => a.StartsWith("@") ? Loc.Get(a.Substring(1)) : a;
    }
}
