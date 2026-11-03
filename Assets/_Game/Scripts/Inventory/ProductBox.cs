using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>입고 박스. 진열대 앞에서 누르고 있으면 한 개씩 채워진다. 비면 쓰레기.</summary>
    public class ProductBox : Carryable
    {
        public NetworkVariable<int> ProductIndex = new(-1);
        public NetworkVariable<int> Remaining = new(0);
        public Renderer labelRenderer;

        public ProductData Product => GameRoot.Instance.products.Get(ProductIndex.Value);
        public override bool IsTrash => Remaining.Value <= 0;

        public override string DisplayName
        {
            get
            {
                if (IsTrash) return Loc.Get("item_empty_box");
                var p = Product;
                return p == null ? Loc.Get("item_box") : $"{Loc.Get(p.nameKey)} x{Remaining.Value}";
            }
        }

        public void ServerInit(int productIndex, int count)
        {
            ProductIndex.Value = productIndex;
            Remaining.Value = count;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ProductIndex.OnValueChanged += (_, _) => Tint();
            Remaining.OnValueChanged += (_, _) => Tint();
            Tint();
        }

        void Tint()
        {
            if (!labelRenderer) return;
            var p = Product;
            var c = IsTrash || p == null ? Color.gray : p.fallbackColor;
            var block = new MaterialPropertyBlock();
            labelRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            labelRenderer.SetPropertyBlock(block);
        }
    }
}
