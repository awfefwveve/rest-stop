namespace RestStop
{
    /// <summary>분리수거장. 빈 박스와 쓰레기봉투를 여기 버려야 사라진다.</summary>
    public class Dumpster : Interactable
    {
        public override string GetPrompt(PlayerController p)
        {
            var h = p.Held;
            return h != null && h.IsTrash ? Loc.Get("prompt_dump", h.DisplayName) : Loc.Get("prompt_dumpster");
        }

        public override void ServerInteract(PlayerController p, InteractKind kind)
        {
            var h = p.Held;
            if (kind != InteractKind.Primary || h == null || !h.IsTrash) return;
            h.ServerConsume();
        }
    }
}
