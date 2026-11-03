using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>손님 한 종류. 프리팹 + 이 데이터만 넣으면 가챠에 들어간다.</summary>
    [CreateAssetMenu(menuName = "RestStop/Customer")]
    public class CustomerData : ScriptableObject
    {
        public string id;
        public string nameKey;
        public Rarity rarity;
        public float weight = 1f;               // 같은 희귀도 안에서의 비중

        [Header("등장 조건")]
        public float minReputation;
        public float hourFrom = 0f, hourTo = 24f;
        public string requiresEquipmentId;      // 이 설비가 있어야 등장

        [Header("구매 성향")]
        public ProductCategory[] likes;
        public int itemsMin = 1, itemsMax = 3;
        [Range(0, 1)] public float equipmentOrderChance = 0.3f;
        [Range(0, 1)] public float foodCourtChance = 0.3f;

        [Header("성격")]
        public float patience = 60f;            // 줄 서서 버티는 초
        [Range(0, 1)] public float messiness = 0.1f;
        [Range(0, 1)] public float eventChance = 0.2f;
        public EventData[] events;
        public string idleLineKey;

        public NetworkObject prefab;
    }
}
