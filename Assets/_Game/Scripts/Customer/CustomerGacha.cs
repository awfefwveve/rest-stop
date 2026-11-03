using System.Collections.Generic;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 문 열릴 때마다 한 번 뽑는다. 희귀도 먼저, 그 안에서 조건 맞는 손님을 비중대로.
    /// 평판이 높을수록 희귀 이상 확률이 오른다.
    /// </summary>
    public static class CustomerGacha
    {
        static readonly List<CustomerData> _pool = new();

        public static float[] Weights(float reputation)
        {
            float r = Mathf.Clamp01(reputation / 100f);
            return new[] { 80f - 10f * r, 15f + 6f * r, 4f + 3f * r, 1f + 1f * r };
        }

        public static Rarity RollRarity(float reputation)
        {
            var w = Weights(reputation);
            float total = 0f;
            foreach (var x in w) total += x;
            float pick = Random.value * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (pick < w[i]) return (Rarity)i;
                pick -= w[i];
            }
            return Rarity.Common;
        }

        public static CustomerData Roll(CustomerDatabase db, float reputation, float hour, bool forceEpic, HashSet<string> equipmentIds)
        {
            var rarity = forceEpic ? Rarity.Epic : RollRarity(reputation);
            // 해당 희귀도에 조건 맞는 손님이 없으면 한 단계씩 내려간다
            for (int r = (int)rarity; r >= 0; r--)
            {
                _pool.Clear();
                float total = 0f;
                foreach (var c in db.items)
                {
                    if (c == null || c.prefab == null || (int)c.rarity != r) continue;
                    if (reputation < c.minReputation) continue;
                    if (hour < c.hourFrom || hour >= c.hourTo) continue;
                    if (!string.IsNullOrEmpty(c.requiresEquipmentId) && !equipmentIds.Contains(c.requiresEquipmentId)) continue;
                    _pool.Add(c);
                    total += c.weight;
                }
                if (_pool.Count == 0) continue;
                float pick = Random.value * total;
                foreach (var c in _pool)
                {
                    if (pick < c.weight) return c;
                    pick -= c.weight;
                }
                return _pool[^1];
            }
            return null;
        }
    }
}
