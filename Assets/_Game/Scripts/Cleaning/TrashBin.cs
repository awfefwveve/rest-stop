using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>쓰레기통. 가득 차면 청결이 떨어지고, 더 버리는 쓰레기는 바닥에 쌓인다.</summary>
    public class TrashBin : Interactable
    {
        public static readonly List<TrashBin> All = new();

        public int capacity = 8;
        public Transform fillVisual;
        public NetworkVariable<int> Fill = new();

        public bool IsFull => Fill.Value >= capacity;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Fill.OnValueChanged += (_, v) => Show(v);
            Show(Fill.Value);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        void Show(int v)
        {
            if (fillVisual) fillVisual.localScale = new Vector3(1f, Mathf.Max(0.02f, (float)v / capacity), 1f);
        }

        public override string GetPrompt(PlayerController p)
        {
            if (p.Held != null) return "";
            if (Fill.Value == 0) return Loc.Get("prompt_bin_empty");
            return Loc.Get(IsFull ? "prompt_bin_full" : "prompt_bin_take", Fill.Value, capacity);
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (kind != InteractKind.Primary || p.Held != null || Fill.Value == 0) return;
            Fill.Value = 0;
            var bag = Instantiate(GameRoot.Instance.trashBagPrefab, p.holdPoint.position, p.holdPoint.rotation);
            bag.NetworkObject.Spawn(true);
            p.ServerHold(bag);
        }

        public void ServerAdd(int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (IsFull) Mess.ServerSpawn(transform.position + Random.insideUnitSphere * 0.8f);
                else Fill.Value++;
            }
        }

        public static TrashBin ServerNearest(Vector3 pos)
        {
            TrashBin best = null;
            float d = float.MaxValue;
            foreach (var b in All)
            {
                float x = (b.transform.position - pos).sqrMagnitude;
                if (x < d) { d = x; best = b; }
            }
            return best;
        }
    }
}
