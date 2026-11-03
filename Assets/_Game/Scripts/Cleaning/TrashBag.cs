namespace RestStop
{
    /// <summary>쓰레기통에서 꺼낸 봉투. 분리수거장(Dumpster)에 버린다.</summary>
    public class TrashBag : Carryable
    {
        public override bool IsTrash => true;
        public override string DisplayName => Loc.Get("item_trash_bag");
    }
}
