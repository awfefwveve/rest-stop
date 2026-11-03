# 휴게소 러시 (가제) — 프로토타입 v0.1

1~4인 협동 고속도로 휴게소 운영 시뮬. Unity 6 LTS + URP + Netcode for GameObjects.
지금은 회색 박스(그레이박스) 상태로 **하루 루프 전체가 도는 것**을 목표로 만든 첫 버전입니다.

## 셋업 (처음 한 번, 10분)

1. Unity Hub에서 **Unity 6 LTS**로 새 프로젝트 생성 → 템플릿 **Universal 3D (URP)**
2. 이 폴더의 `Assets/_Game` 폴더를 새 프로젝트의 `Assets` 안에 그대로 복사
3. `Window > Package Manager > + > Install package by name` 으로 아래 4개 설치
   - `com.unity.netcode.gameobjects`
   - `com.unity.ai.navigation`
   - `com.unity.inputsystem` (이미 있으면 생략)
   - `com.unity.multiplayer.playmode` (멀티 테스트용)
4. `Edit > Project Settings > Player > Active Input Handling` → **Input System Package (New)** 또는 **Both**
5. 컴파일이 끝나면 메뉴 **RestStop > 프로토타입 전체 생성** 클릭
   → 샘플 데이터, 프리팹, `Assets/_Game/Scenes/RestStop_Proto` 씬, 네비메시가 자동 생성됨
6. 씬을 열고 Play → **가게 열기 (호스트)**

### 멀티 테스트 (한 컴퓨터에서 4명)
`Window > Multiplayer > Multiplayer Play Mode` 에서 플레이어 2~4 체크 → Play.
첫 창은 호스트, 나머지 창은 **친구 가게에 참가** (IP 127.0.0.1).
다른 컴퓨터끼리는 호스트의 IP를 입력 (같은 공유기 안). 인터넷 너머 접속은 스팀 트랜스포트로 교체 후 지원.

## 조작
| 키 | 동작 |
| --- | --- |
| WASD / Shift | 이동 / 달리기 |
| 좌클릭 또는 E | 사용 (누르고 있으면 반복: 진열 채우기, 청소) |
| 우클릭 또는 R | 보조 (진열대에서 되담기, 이벤트 거절) |
| G | 들고 있는 물건 내려놓기 |
| Esc | 커서 풀기/잠그기, 주문창 닫기 |
| F5 / F6 (호스트) | 영업 시작 / 다음 날 |
게임패드도 기본 대응 (왼스틱 이동, 오른스틱 시점, A 사용, X 보조, B 내려놓기).

## 하루 흐름
1. **오픈 준비**: 트럭이 창고에 박스를 내려놓음 → 박스 들고 진열대로 → 누르고 있으면 채워짐 (상온/냉장/냉동 구분)
2. **영업 (F5)**: 07시~23시 = 실제 12분. 손님이 가챠로 입장, 2.5시간마다 관광버스 웨이브
   - 편의점 계산대: 클릭 한 번에 물건 하나 계산. 오뎅·군고구마 주문은 설비에서 조리해 둬야 계산됨
   - 식당가 계산대: 라면·우동 조리가 끝나야 계산. 손님은 테이블에서 먹고 가끔 흘림
   - 청소: 바닥 오염 닦기, 쓰레기통 비우기 → 봉투를 분리수거장으로. 빈 박스도 분리수거장으로
   - 물류: 창고 태블릿에서 주문 (일반 = 다음 날 아침, 급배송 = 30% 비싸고 2시간 뒤)
3. **마감 정산**: 매출·원가·폐기·설비비·고정비·이벤트 손익. 자동 저장 → **다음 날 (F6)**

## 폴더 구조
```
Assets/_Game/
  Scripts/
    Core/        GameRoot(메뉴·접속), DayCycle, LevelLayout, SpawnMarker, SaveSystem, Loc(다국어), Announcer(알림)
    Player/      PlayerController, Interactable(상호작용 베이스), Carryable(들기), OwnerNetworkTransform
    Inventory/   ShelfSlot(진열 칸), ProductBox
    Order/       OrderTerminal(태블릿), DeliveryService(입고)
    Customer/    CustomerAI, CustomerGacha, CustomerSpawner(트리클+버스), CodexState(도감·천장)
    Checkout/    CheckoutCounter (편의점·식당 공용)
    Cleaning/    Mess, TrashBin, TrashBag, Dumpster
    Equipment/   CookStation (오뎅바·군고구마·라면·우동 공용)
    Event/       EventRunner
    Economy/     Economy, DailyReport
    Data/        ScriptableObject 정의 (Product, Customer, Equipment, Event)
    UI/          HUD (프로토타입 IMGUI)
    Editor/      RestStopBuilder (자동 생성 메뉴)
  Resources/Localization/strings.csv   16개 언어 테이블
```

## 콘텐츠 추가 방법
- **손님**: `Data/Customers`에 CustomerData 추가 → 프리팹 지정 → CustomerDatabase 목록에 넣기 → GameRoot의 Network Prefabs에 프리팹 추가 → strings.csv에 `cust_<id>`, `cust_<id>_line` 추가
- **상품**: ProductData 추가 → ProductDatabase에 넣기 → `prod_<id>` 번역 추가
- **설비**: EquipmentData + 프리팹(CookStation) → SpawnMarker로 배치
- **이벤트**: EventData (SpawnMess / BlockUntilPoked / StrangeCoin) → 손님 데이터의 events에 연결
- 자동 생성 메뉴를 다시 누르면 샘플 데이터 값이 초기화되니, 직접 수정을 시작한 뒤에는 누르지 마세요.

## 네트워크 구조 (요약)
- 호스트(방장) 권한. 손님 AI·재고·정산·가챠·이벤트는 호스트만 계산
- 플레이어 이동만 각자 계산 (입력 지연 없음)
- 진열된 상품은 네트워크 오브젝트가 아님. 칸마다 상품 번호와 수량만 동기화
- 영업 중엔 입장 불가, 최대 4인. 세이브는 호스트 컴퓨터에만 (`Application.persistentDataPath`)

## 다국어
`strings.csv` 한 장: key, en, ko, ja, zh-Hans, zh-Hant, fr, de, es, es-419, pt-BR, ru, it, pl, tr, th, vi.
현재 영어·한국어만 채워져 있고, 나머지 칸은 비어 있으면 영어로 표시됩니다. 텍스트가 거의 확정되는 시점에 일괄 번역합니다.
메인 메뉴 **언어** 버튼으로 전환.

## 아직 안 된 것 (다음 단계)
- 스팀 트랜스포트 교체 (인터넷 너머 친구 초대), 업적, 클라우드 세이브
- UI를 IMGUI → uGUI/TextMeshPro로 교체 (CJK·태국어 폰트 포함)
- 실제 에셋 교체 (문서의 에셋 규격 참고), 사운드, 손님 애니메이션
- 판매가 조정, 진열대·설비 구매/배치, 매장 확장
- Enter Play Mode Options에서 도메인 리로드를 끄지 마세요 (static 목록 초기화 안 됨)
- 저장되지 않는 것: 바닥 오염, 쓰레기통 양, 들고 있던 물건 (하루 끝에 정리하는 전제)
