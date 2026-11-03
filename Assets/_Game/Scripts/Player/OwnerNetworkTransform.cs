using Unity.Netcode.Components;

namespace RestStop
{
    /// <summary>플레이어 이동은 각자 컴퓨터가 결정하고 나머지에게 보낸다 (입력 지연 없음).</summary>
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
