using Unity.Netcode;

namespace RestStop
{
    /// <summary>
    /// 플레이어가 바라보고 누를 수 있는 모든 것의 베이스.
    /// 판정은 호스트에서 한다 (ServerInteract). UI만 여는 것은 LocalOnly.
    /// </summary>
    public abstract class Interactable : NetworkBehaviour
    {
        /// <summary>true면 RPC 없이 내 화면에서만 처리 (예: 주문 태블릿 UI 열기).</summary>
        public virtual bool LocalOnly => false;
        /// <summary>true면 버튼을 누르고 있는 동안 반복 실행 (진열 채우기, 청소).</summary>
        public virtual bool RepeatOnHold => false;

        public virtual string GetPrompt(PlayerController p) => "";
        public virtual void LocalInteract(PlayerController p, InteractKind kind) { }
        public virtual void ServerInteract(PlayerController p, InteractKind kind) { }
    }
}
