using System.Collections.Generic;
using UnityEngine;

namespace RestStop
{
    /// <summary>네트워크로는 상품을 인덱스로만 주고받는다.</summary>
    [CreateAssetMenu(menuName = "RestStop/Product Database")]
    public class ProductDatabase : ScriptableObject
    {
        public List<ProductData> items = new();

        public ProductData Get(int index) => index >= 0 && index < items.Count ? items[index] : null;
        public int IndexOf(ProductData p) => items.IndexOf(p);
        public int IndexOfId(string id) => items.FindIndex(p => p.id == id);
    }
}
