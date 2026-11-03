using UnityEngine;

namespace RestStop
{
    [CreateAssetMenu(menuName = "RestStop/Product")]
    public class ProductData : ScriptableObject
    {
        public string id;
        public string nameKey;              // Loc 키. 실제 브랜드명 금지
        public ProductCategory category;
        public StorageType storage;
        public int cost = 800;              // 개당 원가 (원)
        public int price = 1500;            // 개당 판매가 (원)
        public int unitsPerBox = 12;
        public int maxPerSlot = 12;
        public int shelfLifeDays;           // 0 = 무기한
        public int unlockLevel = 1;
        public GameObject displayPrefab;    // 진열용 (없으면 색 큐브)
        public Color fallbackColor = Color.white;
    }
}
