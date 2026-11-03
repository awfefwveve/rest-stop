using Unity.Netcode;

namespace RestStop
{
    /// <summary>마감 정산 한 장. int만 있어서 그대로 네트워크로 보낼 수 있다.</summary>
    public struct DailyReport : INetworkSerializeByMemcpy
    {
        public int day;
        public int revenue;      // 매출
        public int cogs;         // 판매 원가
        public int waste;        // 폐기
        public int equipment;    // 설비 운영비
        public int fixedCost;    // 임대료 + 전기료
        public int events;       // 이벤트 손익
        public int net;          // 순이익
        public int customers;    // 계산 완료 손님
        public int angry;        // 화나서 나간 손님
        public int newFaces;     // 도감 신규
    }
}
