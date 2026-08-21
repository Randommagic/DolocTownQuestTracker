# Doloc Town 퀘스트 UI 모드 개발 — 프로젝트 컨텍스트

## 목표

Doloc Town(Steam App ID `2285550`)용 모드 제작.

**만들고 싶은 것:** 현재 진행 중인 퀘스트에 대해 "어느 NPC에게 / 어떤 아이템을 / 몇 개 가져가야 하는지"를
게임 화면에 오버레이 UI로 표시하는 모드.

---

## 지금까지 확인한 사실

### 1. 공식 모딩 시스템으로는 불가능

공식 Steam Workshop 모딩 가이드(Google Docs, 307p) 전문을 확인함. 결론:

- 공식 모딩은 **JSON 데이터 정의 + PNG 텍스처 교체** 방식만 지원
- 지원 범위: 아이템, 장비, 레시피, 헤드기어, 건물, 플랫폼, 차량, 작물, 물고기, 요리, 자원, 상점 아이템, 로컬라이제이션
- **문서 전체에 "quest" 단어가 단 한 번도 등장하지 않음**
- 스크립팅 API 없음 (Lua/C#/DLL 훅 전무)
- 커스텀 UI를 그리는 수단 없음 (기존 스프라이트 교체만 가능)
- 런타임 게임 상태 접근 불가

공식 모드 폴더 구조 (참고용):
```
C:\Users\[사용자]\AppData\LocalLow\RedSawGames\DolocTown\MODS\[내모드]\
  ├─ info.json      (필수, 이게 있어야 모드로 인식)
  ├─ preview.png    (워크샵 미리보기)
  ├─ icon.png       (32x32, 인게임 모드 메뉴 아이콘)
  └─ Content\       (모든 JSON + 텍스처)
```

### 2. 해결책: DTMAPI (BepInEx 기반) 경로

- **DTMAPI**: 커뮤니티(제작자 Yuuka)가 만든 비공식 런타임 프레임워크
  - Steam Workshop ID: `3743016467`
  - 설치: 구독 후 `\steamapps\workshop\content\2285550\3743016467\1_install_dtmapi.bat` 실행
  - 이게 **BepInEx 포함 모드 로더**를 설치해줌
  - 제공 기능: config menu, log diagnostics, Manager status pages, report export, 기능 모드용 런타임 지원
  - 현재 DTMAPI에 의존하는 모드가 23개 존재

- **게임은 Unity Mono 빌드** → 다음이 모두 가능:
  - `Assembly-CSharp.dll` 디컴파일
  - Harmony로 임의 메서드 패치
  - Unity UI(uGUI/IMGUI)로 커스텀 오버레이 렌더링

- **공식 지원 아님 주의**: 개발사가 "DTMAPI를 요구하는 기능 모드는 핵심 게임 메커니즘을 바꾸므로
  모드 프레임워크 하에서 공식 지원되지 않으며, 최신 버전 호환을 보장할 수 없다"고 명시.
  게임 업데이트마다 Harmony 패치 타겟이 깨질 수 있음 → 방어적으로 코딩할 것.

### 3. 핵심 레퍼런스: 미니맵 오버레이 모드

한국 커뮤니티(DC 도로록타운 갤러리)에 공개된 모드. **원하는 모드와 구조가 거의 동일함.**

- Steam Workshop ID: `3782262739`
- 최종 버전: v2.8.0 (2026.08.12)
- 설치 위치: `\Doloc Town\BepInEx\plugins\DolocMinimap\`
- 설정 파일: `\Doloc Town\BepInEx\config\local.dolocminimap.cfg`
  → BepInPlugin GUID가 `local.dolocminimap`인 것을 알 수 있음
- 조작: `F9` 설정창 토글, `N` 미니맵 토글
- 인게임 모드 매니저에는 표시되지 않음 (순수 BepInEx 플러그인으로 동작)
- 배포 형태 2가지: `ModOnly`(BepInEx 기설치자용) / `FullBundle`(BepInEx 포함)

**구조적 대응 관계:**

| 미니맵 모드 | 만들려는 퀘스트 UI 모드 |
|---|---|
| 게임 내부 미니맵 데이터 읽기 | 게임 내부 퀘스트 데이터 읽기 |
| 화면에 오버레이 렌더링 | 화면에 퀘스트 UI 렌더링 |
| N키 토글 / F9 설정창 | 동일 패턴 |
| BepInEx ConfigFile로 단축키 설정 | 동일 |
| DTMAPI 기반 + 직접설치 양쪽 배포 | 동일 전략 |

### 4. 기타 참고 모드

- **DolocPlus** — BepInEx + Cheat Engine 기반. "게임 데이터 파싱" 기능 포함.
  이미 게임 내부 자료구조를 분석해둔 결과물이 있을 가능성 높음.
  Steam 가이드 ID: `3477767443`
- **Quality of Doloc** (`3780486383`) — DTMAPI 내장 config menu 연동 사례
- **DTMAPI Auto Fishing** (Yuuka 본인 작) — DTMAPI 사용 샘플격
- **Magic Storage** (Minato Aquin)
- **Doloc Town QoL** (CloudsWhite)

---

## 현재 상태

ILSpy(VS Code 확장)로 어셈블리를 디컴파일해 프로젝트로 export 완료.

### 다음에 할 일

#### 1단계 — 미니맵 모드 분석 (`DolocMinimap.dll` export 결과)

확인할 것:
- [ ] `[BepInPlugin(...)]` 어트리뷰트 붙은 진입점 클래스
- [ ] `[BepInDependency(...)]` 있는지 → DTMAPI가 필수 의존인지 선택인지 판단
- [ ] `Awake()` 내용 — Harmony 인스턴스 생성/패치하는지, 단순 폴링인지
- [ ] **게임 데이터 접근 방식** ← 가장 중요
  - `GameObject.Find()` / `FindObjectOfType<T>()` 같은 Unity 탐색인지
  - 게임 싱글톤 매니저(`XxxManager.Instance`)에 직접 접근인지
  - 리플렉션(`AccessTools.Field`)인지 직접 타입 참조인지
  - → 여기서 찾은 "게임 매니저 진입 경로"가 퀘스트 데이터 탐색의 출발점이 됨
- [ ] UI 렌더링 방식 — `OnGUI()` IMGUI인지, uGUI Canvas 동적 생성인지
- [ ] `Config.Bind<KeyboardShortcut>(...)` 설정 패턴

#### 2단계 — 퀘스트 데이터 구조 찾기 (`Assembly-CSharp.dll`)

검색 키워드:
```
Quest, Task, Mission, Order, Commission, Request, Delivery, Objective
TbQuest, TbTask, tbquest   ← 게임이 Tb(테이블) 접두사 규칙을 씀
requiredItems, targetNpc, itemCount, progress, currentCount
```

찾아야 할 최종 형태(추정):
```
활성 퀘스트[] → { npcId, requirements[] → { itemId, needCount, currentCount } }
```

#### 3단계 — 모드 구현

미니맵 모드 구조를 베이스로 데이터 소스만 교체.

---

## 알아두면 좋은 게임 내부 ID 규칙 (공식 모딩 문서에서)

텍스처/에셋 네이밍 규칙이 내부 ID 체계를 반영함:

- 아이템 아이콘: `icon_item_[ITEM_ID]` (28x28)
- NPC 애니메이션: `anim_npc_[NPC_ID]_idle_[0~5]`
- NPC 위키 아이콘: `icon_document_[NPC_ID]`
- NPC 캘린더 아이콘: `icon_calendar_[NPC_ID]`

**알려진 NPC ID 목록:**
```
claud, kel, kenenimuu, gabyrl, kasia, mody, shylock, quipu, orlando,
witch, paiea, kuma, lank, gerald, cod, merlin, zenis, pike, alchemy,
lightman, mira, sacco, dada, hult, villain, abel
```

**주요 데이터 테이블 파일명 패턴** (JSON 모딩에서 쓰이는 이름 = 내부 테이블 클래스명 힌트):
```
item_tbitem.json          아이템
player_tbhat.json         모자
equipment_tbequipment.json 장비
recipe_tbrecipe.json      레시피
plant_tbseed.json         작물
fishing_tbfish.json       물고기
resource_tbresource.json  자원
building_tbbuilding*.json 건물
localization_tbtextmapper[en|zh_cn|zh_tw].json  로컬라이제이션
```
→ 퀘스트 테이블도 `quest_tbquest` 같은 이름일 가능성이 있음. 검색해볼 것.

---

## 배포 계획 (나중에)

미니맵 모드와 동일하게:
1. 직접 설치용 — `\Doloc Town\BepInEx\plugins\[모드명]\` 에 넣는 폴더 배포
2. Steam Workshop — DTMAPI 의존 모드로 등록

Workshop 업로드는 게임 내 Mods 메뉴 → Local 탭 → Upload Mod 버튼.
업로드 후 `workshop.json`이 생성되며 여기 `workshop_id`가 저장됨 (수동 수정 금지).
