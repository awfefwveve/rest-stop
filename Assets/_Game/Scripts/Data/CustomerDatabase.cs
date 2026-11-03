using System.Collections.Generic;
using UnityEngine;

namespace RestStop
{
    [CreateAssetMenu(menuName = "RestStop/Customer Database")]
    public class CustomerDatabase : ScriptableObject
    {
        public List<CustomerData> items = new();
        public CustomerData Get(int index) => index >= 0 && index < items.Count ? items[index] : null;
        public int IndexOf(CustomerData c) => items.IndexOf(c);
    }
}
