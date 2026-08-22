# DolocTownQuestTracker — 남은 확인/작업 목록

> 2026-08-22 기준. 실기 확인 가능한 시나리오는 전부 통과.
> 남은 둘은 도달 불가(자금 전용 미션)와 출시 후 확인 예정(긴 세션)뿐이다.
> 설계·데이터 조사 배경은 `RESEARCH.md` 참조.

## 시나리오 확인

- [ ] **돈만 요구하는 미션** — `doloc_trading_port`는 아이템 없이 자금 5000만 필요.
      해당 미션에 도달하지 못해 실기 확인 불가. 아이템+자금이 함께 있는 미션은
      정상 표시되므로 자금 줄 자체는 동작한다. 남은 미지수는 "아이템 줄이
      0개일 때 레이아웃"뿐
- [x] **팩션 미션을 완료하는 순간** — `SUBMIT_ITEM` 경로. 오버레이에서 잘 사라짐
- [x] **종자 연구를 완료하는 순간** — `UNLOCK_SEED` 경로. 오버레이에서 잘 사라짐
- [x] **세이브 전환** — 확인 완료. 다른 세이브의 선택을 지우지는 않지만,
      **반대 문제가 드러남**: 세이브 1에서 켠 퀘스트가 세이브 2에서도 그대로 보인다.
      아래 "세이브별 추적 목록" 항목 참고
- [x] **타이틀 복귀 후 재진입** — 스위치가 정상적으로 다시 생성됨.
      토글 목록이 `static`이라 타이틀로 나가면 패널과 함께 토글이 파괴되는데,
      목록은 살아남아 Unity-null 참조를 들고 있다. `SeedCheckbox.EnsureToggles`의
      staleness 검사(개수가 아니라 파괴 여부를 본다)가 의도대로 동작하는 것을 확인
- [x] **다른 해상도 / 창 모드** — 확인 완료. 다른 해상도에서 우측 여백 유지됨
- [x] **5개 제한 경계** — 확인 완료. 일부 완료 시 빈 슬롯이 생김
- [ ] **긴 세션 안정성** — 장시간 플레이 시 팁 풀 누수나 성능 저하 여부.
      1.0.0 출시 후 실사용으로 확인 예정
- [x] **`/simplify` 정리 후 회귀 확인** — 회귀 없음.
      아이템 개수 즉시 갱신(줍기·버리기), 마퀴 스크롤, 5개→1개 축소 시 잔여 팁 없음,
      로그에 예외 없음

## 남은 작업

- [x] **세이브별 추적 목록** — 구현 및 실기 검증 완료.
      `슬롯/지문=목록` 형식으로 슬롯별 분리. 슬롯 번호는 재사용되므로
      **세이브 파일의 생성 시각**을 지문으로 삼는다 (세이브 안에는 uuid도
      생성시각도 없다). 게임은 저장을 반복해도 파일 생성 시각을 보존하고,
      삭제는 `.bak` 이름 변경이라 새 게임은 새 생성 시각을 얻는다.
      검증한 시나리오: 세이브별 분리 / 복제본 빈 목록 / 복제본→원본 복귀 /
      삭제 후 같은 자리 새 게임 / 구 평문 형식 이관.
      `archiveIndex`가 파일명 번호와 일치하는 것도 확인됨
- [x] **설정 파일 잔여 항목 정리** — GUID가 `Randommagic.doloctownquesttracker`로 바뀌면서
      cfg 파일이 새로 생성되므로 죽은 키(`ListKey`, `OverlayToggleKey`, `DebugDumpKey`,
      `Verbose`, `Width`, `MaxLines` 등)가 자연히 사라짐
- [x] **워크샵 패키지 골격** — `workshop/` + `package.ps1`.
      게임에 설치된 창작마당 항목들을 뜯어 형식 확인
      (`info.json` / `workshop.json` / `icon.png` / `preview.png` / `Content/`)
- [x] **아이콘·미리보기 이미지** — `workshop/icon.png`, `workshop/preview.png` 추가 완료
- [ ] **최초 업로드 후 `workshop_id` 기록** — 업로드하면 발급되는 id를
      `workshop/workshop.json`에 적어야 이후 갱신이 같은 항목으로 올라감
- [x] **DTMAPI CodeMod로 포팅** — BepInEx 플러그인 경로를 버리고 DTMAPI 전용 단일 경로로.
      `BepInPlugin`→`DtmMod`, `ConfigEntry<T>`→`ModConfig` POCO + `IConfigHelper`,
      `ManualLogSource`→`IMonitor`, 설정은 DTMAPI 인게임 설정 페이지에 노출.
      데이터 조사·오버레이·Harmony 패치는 무변경. 규칙 근거는 `PACKAGING.md` 참조
- [x] **인게임 로드 검증** — 2026-08-22 실기 확인. DTMAPI 로그 근거:
      `Discovered 3 DTMAPI-capable mod folder(s)` + `source=Local` 경로로 로드,
      `Official mod id = Local.DolocTownQuestTracker`, `Quest Tracker 1.0.0 loaded.`,
      `Mod Entry completed.`, `Tip prefab width = 450`(오버레이 구축, 블로커 로그 없음),
      `ConfigMenuPage:registered=1`(DTMAPI 설정 페이지 등록).
      설정 왕복도 확인: 시작 시 `Core initialized. watchlist="..."`가 디스크 내용과 일치하고,
      인게임 토글 한 번에 `Watchlist`에 항목이 추가되며 파일이 갱신된다.
      경고·에러 0건. 껐던 미니맵 모드가 `Skipping`되는 것으로 DTMAPI의 콜드 스타트
      활성 판정도 함께 확인됨
- [x] **끄면 즉시 조용해지게** — DTMAPI는 게임의 Mod 화면을 열면 재검사(`DiscoverMods`)까지
      하지만 이미 로드된 CodeMod을 내리지는 못한다(`LoadMods hot loadedNow=0`).
      그래서 화면에 그리는 모드는 끈 뒤에도 계속 보인다. `ModEnablement`가 게임의 라이브
      상태(`DolocAPI.modManager.GetAllEnabledModInfos()`, 전부 public)를 1초 간격으로 확인해
      오버레이와 패널 토글을 멈춘다. 소프트 디스에이블이다 — 패치는 붙어 있고 재시작
      전까지 언로드되지 않는다. 상태를 못 읽으면 켜진 것으로 간주한다(숨는 쪽이 더 나쁨).
      실기 확인: `Switched off in the Mods page; going quiet.` →
      `Switched on in the Mods page; resuming.` 재시작 없이 양방향 동작
- [x] **`/simplify` 실행** — 2회 (2026-08-22). 2회차에서 나왔지만 넘긴 것:
      추적 목록(`slot.Keys`)이 완료 항목을 계속 쌓아 무한히 자라는 문제.
      오래된 것부터 자르면 아직 추적 중인 항목을 해제할 수 있어(삽입 순서상
      오래 추적한 것이 가장 오래된 것일 수 있다) 스냅샷을 `Prune`에 넘겨
      "살아있지 않은 것만" 자르는 설계가 필요하다 → 아래 항목에서 해결
- [x] **추적 목록 무한 증가 해결** — `QuestSnapshot.Finished`에 완료 신호(팩션
      `m.IsComplete` / 종자 `IsSeedNodeUnlocked`)를 담아 `Build()`에서 채우고,
      `Toggle`이 이미 들고 있던 스냅샷을 `Prune`에 그대로 넘겨 그 안에 있는 키만
      지운다. "현재 스냅샷에 없음"(해금 전 포함) 대신 "완료로 확인됨"만 기준으로
      삼아 아직 추적 중인 항목은 건드리지 않는다. `Prune`은 토글 시점에만 돌므로
      완료 후 다음 토글까지는 파일에 남아있을 수 있음(기존 `IdExists` 정리와 동일한 지연)
- [x] **네임스페이스를 `DolocTownQuestTracker`로 변경, 버전 1.0.0**
- [ ] **`RESEARCH.md` 갱신** — 설계가 크게 바뀐 부분 반영
      (별도 리스트 창 없음 / 게임 패널 토글 / 마퀴 / 단축키 없음)

## 알려진 제약 (의도된 것)

- **세이브 식별은 파일 생성 시각에 의존** — 세이브를 지우고 15초 안에 같은 자리에
  새 게임을 만들어 저장까지 마치면, NTFS 파일명 터널링이 옛 생성 시각을 되살려
  같은 세이브로 오인할 수 있다. 캐릭터 생성 절차상 현실적으로 불가능하다고 보고
  대응하지 않음. 세이브 파일을 못 찾는 경우에는 아무것도 지우지 않는다

- **아이템 부분 제출 표시 불가** — 게임이 전량 일괄 제출만 허용하므로 중간값이 존재하지 않음.
  줄 상태는 완료 / 제출가능 / 부족 3가지
- **보유 개수는 가방만** — 기본값 `CountNearbyContainers = false`.
  true로 하면 게임의 제출 판정과 같아지지만 방을 옮길 때마다 숫자가 흔들림
- **추적 최대 5개** — 두 탭 합산
- **팩션 패널은 한 번에 미션 하나** — 게임 구조(`DolocSinglePageUI`)상 토글도 현재 보는 미션 하나에 대응
- **세로 마퀴 없음** — 시도했으나 읽는 흐름이 나빠서 가로 1줄로 확정
