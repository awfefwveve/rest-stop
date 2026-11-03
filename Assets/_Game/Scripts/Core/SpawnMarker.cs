using Unity.Netcode;
using UnityEngine;

namespace RestStop
{
    /// <summary>호스트가 이 위치에 prefab을 스폰한다. 이름은 씬 안에서 유일해야 한다.</summary>
    public class SpawnMarker : MonoBehaviour
    {
        public NetworkObject prefab;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, Vector3.one);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward);
        }
    }
}
