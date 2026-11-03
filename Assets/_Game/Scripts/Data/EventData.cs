using UnityEngine;

namespace RestStop
{
    /// <summary>상황 → 플레이어가 하는 행동 → 결과. 대처는 이미 있는 행동(청소, 상호작용)으로 한다.</summary>
    [CreateAssetMenu(menuName = "RestStop/Event")]
    public class EventData : ScriptableObject
    {
        public string id;
        public string titleKey;
        public string lineKey;          // 손님 말풍선
        public GameEventType type;
        public int messCount = 3;       // SpawnMess
        public float timeLimit = 45f;
        public int successMoney;
        public float successRep = 2f;
        public int failMoney;
        public float failRep = -4f;
    }
}
