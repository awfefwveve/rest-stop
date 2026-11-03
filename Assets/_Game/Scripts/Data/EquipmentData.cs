using UnityEngine;

namespace RestStop
{
    /// <summary>오뎅바·군고구마·라면·우동 등 조리 설비 공통 데이터.</summary>
    [CreateAssetMenu(menuName = "RestStop/Equipment")]
    public class EquipmentData : ScriptableObject
    {
        public string id;
        public string nameKey;
        public Zone zone;
        public int dailyCost = 5000;        // 하루 운영비
        public float cookSeconds = 20f;     // 실제 초
        public float burnSeconds = 40f;     // 완성 후 이 시간이 지나면 탐
        public int batchSize = 4;           // 한 번 조리 개수
        public int unitCost = 500;          // 개당 원가 (폐기 계산용)
        public int price = 2000;            // 개당 판매가
        public int careUses = 5;            // 재료 보충 한 번에 조리 가능 횟수
        public int refillCost = 8000;       // 재료 보충 비용
    }
}
