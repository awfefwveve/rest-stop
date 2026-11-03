using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>씬 안의 위치 정보. 손님 입·퇴장, 버스 정류장, 입고 구역, 좌석 등.</summary>
    public class LevelLayout : MonoBehaviour
    {
        public static LevelLayout Instance { get; private set; }

        public Transform playerSpawn;
        public Transform entrance;      // 걸어 들어오는 손님 (트리클)
        public Transform busStop;       // 버스 손님 웨이브
        public Transform exit;
        public Transform deliveryArea;  // 트럭 박스가 쌓이는 곳
        public Transform[] seats;       // 식당가 좌석

        void Awake() => Instance = this;

        /// <summary>호스트 전용. 씬의 SpawnMarker 위치에 설비 프리팹을 스폰한다.</summary>
        public void SpawnAll()
        {
            var markers = FindObjectsByType<SpawnMarker>(FindObjectsSortMode.None);
            foreach (var m in markers)
            {
                if (m.prefab == null) continue;
                var no = Instantiate(m.prefab, m.transform.position, m.transform.rotation);
                no.name = m.name; // 세이브 키로 사용
                no.Spawn();
            }
        }

        public Vector3 RandomDeliverySpot(int i)
        {
            var b = deliveryArea.position;
            return b + deliveryArea.right * ((i % 4) * 0.7f) + deliveryArea.forward * ((i / 4 % 4) * 0.7f) + Vector3.up * (0.4f * (i / 16));
        }
    }
}
