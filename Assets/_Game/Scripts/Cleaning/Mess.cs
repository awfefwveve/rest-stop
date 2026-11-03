using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace RestStop
{
    /// <summary>바닥 오염 (쏟은 음료, 떨어진 과자, 먹고 남긴 것). 누르고 있으면 닦인다.</summary>
    public class Mess : Interactable
    {
        public static readonly List<Mess> All = new();

        public float cleanPerTick = 0.2f;
        public NetworkVariable<float> Dirt = new(1f);
        public Transform visual;

        public override bool RepeatOnHold => true;
        public override string GetPrompt(PlayerController p) => Loc.Get("prompt_clean", Mathf.CeilToInt(Dirt.Value * 100f));

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Dirt.OnValueChanged += (_, v) => Scale(v);
            Scale(Dirt.Value);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        void Scale(float v)
        {
            if (visual) visual.localScale = new Vector3(0.4f + 0.6f * v, 1f, 0.4f + 0.6f * v);
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            if (kind != InteractKind.Primary) return;
            Dirt.Value -= cleanPerTick;
            if (Dirt.Value <= 0.001f) NetworkObject.Despawn(true);
        }

        public static Mess ServerSpawn(Vector3 pos)
        {
            // 손님 머리 위에 생기지 않도록 길찾기 바닥에 붙인다
            if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas))
                pos = hit.position + Vector3.up * 0.01f;
            var m = Instantiate(GameRoot.Instance.messPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            m.NetworkObject.Spawn(true);
            return m;
        }
    }
}
