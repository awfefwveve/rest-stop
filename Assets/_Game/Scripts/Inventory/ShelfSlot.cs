using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 진열대 한 칸. 상품 하나만 놓인다. 네트워크로는 상품 인덱스와 수량만 보내고,
    /// 진열된 상품 모습은 각자 화면에서 그린다 (네트워크 오브젝트 아님).
    /// Shelf 루트(NetworkObject)의 자식에 붙인다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ShelfSlot : Interactable
    {
        public static readonly List<ShelfSlot> All = new();

        public StorageType storage = StorageType.Ambient;
        public Vector3 area = new(0.9f, 0.3f, 0.35f);   // 진열 공간 (로컬)
        public float accessDistance = 0.9f;               // 손님이 서는 거리

        public NetworkVariable<int> ProductIndex = new(-1);
        public NetworkVariable<int> Count = new(0);
        public NetworkVariable<int> StockedDay = new(0);

        readonly List<GameObject> _shown = new();
        static readonly Dictionary<int, Material> _mats = new();

        public override bool RepeatOnHold => true;
        public ProductData Product => GameRoot.Instance.products.Get(ProductIndex.Value);
        public int Capacity => Product != null ? Product.maxPerSlot : 12;
        public bool HasStock => Count.Value > 0;

        public string SlotKey => $"{NetworkObject.name}/{transform.GetSiblingIndex()}";

        /// <summary>손님이 서는 바닥 위치.</summary>
        public Vector3 AccessPoint
        {
            get
            {
                var p = transform.position + transform.forward * accessDistance;
                p.y = transform.root.position.y;
                return p;
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            ProductIndex.OnValueChanged += (_, _) => Rebuild();
            Count.OnValueChanged += (_, _) => Rebuild();
            Rebuild();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        public override string GetPrompt(PlayerController p)
        {
            var box = p.Held as ProductBox;
            if (box != null && !box.IsTrash)
            {
                var bp = box.Product;
                if (bp.storage != storage) return Loc.Get("prompt_wrong_storage");
                if (ProductIndex.Value >= 0 && ProductIndex.Value != box.ProductIndex.Value) return Loc.Get("prompt_other_product");
                if (Count.Value >= bp.maxPerSlot) return Loc.Get("prompt_slot_full");
                return Loc.Get("prompt_fill", Loc.Get(bp.nameKey));
            }
            if (Product == null) return Loc.Get("prompt_slot_empty", Loc.Get("storage_" + storage));
            return $"{Loc.Get(Product.nameKey)} {Count.Value}/{Capacity}";
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (p.Held is not ProductBox box) return;

            if (kind == InteractKind.Primary)
            {
                if (box.IsTrash) return;
                var bp = box.Product;
                if (bp == null || bp.storage != storage) return;
                if (ProductIndex.Value >= 0 && ProductIndex.Value != box.ProductIndex.Value) return;
                if (Count.Value >= bp.maxPerSlot) return;

                if (ProductIndex.Value < 0) StockedDay.Value = DayCycle.Instance.Day.Value;
                ProductIndex.Value = box.ProductIndex.Value;
                Count.Value++;
                box.Remaining.Value--;
            }
            else if (kind == InteractKind.Secondary)
            {
                // 잘못 넣은 것 다시 박스로
                if (Count.Value <= 0) return;
                if (box.ProductIndex.Value >= 0 && box.ProductIndex.Value != ProductIndex.Value && !box.IsTrash) return;
                box.ProductIndex.Value = ProductIndex.Value;
                box.Remaining.Value++;
                ServerRemoveOne();
            }
        }

        // ---- 호스트 전용 ----

        /// <summary>손님이 한 개 집는다. 상품 인덱스 반환, 없으면 -1.</summary>
        public int ServerTake()
        {
            if (Count.Value <= 0) return -1;
            int idx = ProductIndex.Value;
            ServerRemoveOne();
            return idx;
        }

        void ServerRemoveOne()
        {
            Count.Value--;
            if (Count.Value <= 0) { Count.Value = 0; ProductIndex.Value = -1; }
        }

        /// <summary>유통기한 지난 재고를 비우고 폐기 원가를 반환.</summary>
        public int ServerExpire(int today)
        {
            var p = Product;
            if (p == null || p.shelfLifeDays <= 0) return 0;
            if (today - StockedDay.Value < p.shelfLifeDays) return 0;
            int waste = p.cost * Count.Value;
            Count.Value = 0;
            ProductIndex.Value = -1;
            return waste;
        }

        public void ServerSet(int productIndex, int count, int stockedDay)
        {
            ProductIndex.Value = count > 0 ? productIndex : -1;
            Count.Value = Mathf.Max(0, count);
            StockedDay.Value = stockedDay;
        }

        // ---- 화면 표시 (각자) ----

        void Rebuild()
        {
            var p = Product;
            int n = p == null ? 0 : Count.Value;
            while (_shown.Count > n)
            {
                Destroy(_shown[^1]);
                _shown.RemoveAt(_shown.Count - 1);
            }
            // 상품이 바뀌었으면 모양을 새로 만든다
            if (_shown.Count > 0 && _shown[0].name != (p != null ? p.id : ""))
            {
                foreach (var g in _shown) Destroy(g);
                _shown.Clear();
            }
            const int perRow = 6, depth = 2;
            var cell = new Vector3(area.x / perRow, 0f, area.z / depth);
            while (_shown.Count < n)
            {
                int i = _shown.Count;
                int col = i % perRow, row = i / perRow % depth, layer = i / (perRow * depth);
                var local = new Vector3(-area.x / 2 + cell.x * (col + 0.5f), 0.08f + layer * 0.15f, -area.z / 2 + cell.z * (row + 0.5f));
                _shown.Add(MakeItem(p, local));
            }
        }

        GameObject MakeItem(ProductData p, Vector3 local)
        {
            GameObject g;
            if (p.displayPrefab) g = Instantiate(p.displayPrefab, transform);
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(transform, false);
                g.transform.localScale = new Vector3(0.11f, 0.15f, 0.11f);
                g.GetComponent<Renderer>().sharedMaterial = MatFor(p);
            }
            g.name = p.id;
            g.transform.localPosition = local;
            g.transform.localRotation = Quaternion.identity;
            return g;
        }

        static Material MatFor(ProductData p)
        {
            int key = p.GetInstanceID();
            if (_mats.TryGetValue(key, out var m) && m) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader);
            m.SetColor("_BaseColor", p.fallbackColor);
            m.SetColor("_Color", p.fallbackColor);
            _mats[key] = m;
            return m;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.up * area.y / 2, area);
        }
    }
}
