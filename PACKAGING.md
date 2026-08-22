# 창작마당 업로드 준비

> 내부 문서. 업로드되지 않는다 — 업로드 대상은 `package.ps1`이 만드는 `dist/workshop/`뿐이다.

`workshop/` 폴더가 창작마당 항목의 **원본**이다. `package.ps1`이 여기 있는 파일과 빌드 결과물을 합쳐
`dist/workshop/`에 업로드용 폴더를 만든다.

## 창작마당 항목 구조

설치된 다른 항목들을 뜯어 확인한 구조 (`steamapps\workshop\content\2285550\<id>\`):

```
<항목>/
├─ info.json        이름·작성자·버전·설명·태그 (+ 언어별 번역)
├─ workshop.json    {"workshop_id": <숫자>}  — 최초 업로드 후 발급됨
├─ icon.png         목록용 아이콘
├─ preview.png      상세 페이지 미리보기
└─ Content/         실제 배포물
```

## 이 모드의 배포 방식 — DTMAPI CodeMod

**구독만 하면 설치가 끝난다.** DLL을 손으로 옮길 필요가 없다.

DTMAPI는 시작 시 구독 폴더와 로컬 `MODS` 폴더를 훑어 `Content/DTMAPI/manifest.json`을 찾고,
거기 적힌 `EntryDll`을 로드해 `EntryType`을 인스턴스화한다. 그래서 `Content/`는 이렇게 구성된다:

```
Content/
└─ DTMAPI/
   ├─ manifest.json              DTMAPI가 읽는 모드 선언
   ├─ dtmapi-package.json        {"uniqueId", "version"} 패키지 마커
   └─ DolocTownQuestTracker.dll  진입점 DolocTownQuestTracker.ModEntry
```

`INSTALL.txt`는 없다. 설치 절차 자체가 없어졌다.

### DTMAPI 0.6.1에서 확인한 규칙

`DTMAPI.Core.dll`의 `ManagedModClassifier`를 뜯어 확인한 것들이다. 어기면 모드가 로드되지 않는다.

- `Type`은 **정확히** `CodeMod` 또는 `ContentPack`. 다른 값은 `unknown-manifest-type`으로 거부된다.
- `CodeModKind`는 **적지 않는다.** 생략하면 `LegacyNativeCodeMod`로 분류되어
  `Assembly-CSharp` / `0Harmony` / `UnityEngine*` / `Unity.*` 참조가 허용된다.
  `"Strict"`를 적으면 그 참조들이 전부 금지되고, `"Advanced"`는 서명된 reference receipt를 요구한다.
- manifest 필드 이름은 **대소문자를 구분**하고 각 필드는 정확히 한 번만 나와야 한다
  (`RejectMisCasedOrDuplicateWireField`).
- 어셈블리 이름이 `DTMAPI.*` 중 하나면 `impersonates a native/runtime assembly`로 거부된다.
- 패키지 폴더에 게임·플랫폼 DLL 사본을 넣으면
  `carries a copied platform/game assembly`로 거부된다. 우리 DLL 하나만 넣는다.
- `EntryDll`은 패키지 루트 하위의 상대경로여야 하고 확장자는 `.dll`이어야 한다.
- `TargetFramework`가 정확히 netstandard2.0이어야 한다는 검사는 **Advanced 전용**이다
  (`ValidateCodeModAssemblyBoundary`에서 advanced 인자가 false면 건너뛴다).
  참고로 창작마당의 다른 CodeMod(Quality of Doloc)는 netstandard 2.1로 빌드돼 있고 정상 동작한다.

`package.ps1`이 `info.json` / `manifest.json` / `dtmapi-package.json` / `PluginInfo.cs`의
버전과 UniqueID가 어긋나면 빌드를 세운다. 네 곳이 서로 다른 독자를 상대하므로 손으로 맞추면 반드시 어긋난다.

### 설정 파일

`<게임폴더>\DTMAPI\config\Randommagic.DolocTownQuestTracker.json`.

DTMAPI가 `DataContractJsonSerializer`로 읽고 쓴다. 그래서 `ModConfig`는 public 클래스에
public getter/setter만 쓴다. 사용자는 인게임 DTMAPI 설정 페이지에서 바꾸면 되고 파일을 열 이유가 없다.

## 업로드 절차

게임에 **자체 모드 업로더가 내장**되어 있다. 별도 Steam 도구(SteamCmd, Workshop Uploader)는 필요 없다.

### 로컬 모드 폴더

게임 설치 폴더가 아니라 유저 데이터 쪽이다.

```
%USERPROFILE%\AppData\LocalLow\RedSawGames\DolocTown\MODS\
```

`package.ps1`이 빌드·패키징을 마친 뒤 이 아래에 업로드용 트리를 **직접 배치한다**
(`STAGED` 줄로 알려준다). 손으로 복사할 일은 없다. `build.ps1`은 개발 중 반복 테스트용이라
DLL과 JSON만 갱신하고, 업로드 직전에는 `package.ps1`을 쓴다.

```
MODS\
└─ DolocTownQuestTracker\      ← 이름은 자유
   ├─ info.json
   ├─ icon.png
   ├─ preview.png
   └─ Content\
      └─ DTMAPI\
         ├─ manifest.json
         ├─ dtmapi-package.json
         └─ DolocTownQuestTracker.dll
```

`workshop.json`은 최초 업로드 전에는 없다 (위 "확인된 것과 추정인 것" 참고).

### 게임 내 조작

타이틀 화면의 모드 메뉴(`ui_tip_homepage_mods`)에서:

| 기능 | 문자열 키 |
|---|---|
| 로컬 폴더 열기 | `ui_mod_open_local_directory` |
| 업로드 / 확인 | `ui_mod_upload_mod` / `ui_mod_confirm_upload` |
| 갱신 / 확인 | `ui_mod_update_mod` / `ui_mod_confirm_update` |
| 창작마당 페이지 열기 | `ui_mod_open_workshop` |
| 켜기·끄기, 순서 | `ui_mod_enable` / `ui_mod_disable` / `ui_mod_move_up` / `ui_mod_move_down` |

로컬 모드는 목록에서 `source: Local`로 잡힌다 (`SAVE\mod_infos.json` 참고).
최초 업로드 후에는 같은 화면의 **갱신**을 쓴다. 창작마당 id가 없으면
`[MOD] Workshop update failed: missing workshop id`로 실패한다.

### 확인된 것과 추정인 것

`info.json`과 `preview.png`는 게임 어셈블리의 문자열 리터럴에 있으므로 **게임이 직접 읽는다.**

`icon.png`와 `workshop.json`은 `DolocTown_Data\Managed`의 어떤 어셈블리에도
문자열로 없는데, 설치된 창작마당 항목 4개에는 모두 존재한다.

`workshop.json`은 **게임이 직접 쓴다** — `ModInfo.SetWorkshopId`가
`FileUtils.TrySaveJson<ModWorkshopInfo>`로 모드 루트에 저장한다. 그래서 최초 업로드용
패키지에는 **넣지 않는다.** `{"workshop_id": 0}` 같은 플레이스홀더를 두면
`ModInfo.TryLoadWorkshopInfo`가 그걸 실제 id로 읽어들여, 업로더가 최초 업로드를 갱신으로
오분류하고 `[MOD] Workshop update failed: missing workshop id`로 실패할 위험이 있다
(`SteamWorkshopUploader.UploadMod`의 분기).

최초 업로드 후 게임이 써준 id를 `workshop/workshop.json`에 옮겨 적으면, 그 뒤로는
`package.ps1`이 그 파일을 패키지와 로컬 폴더에 함께 복사한다.

### 언어별 이름·설명은 세 개보다 많이 넣어도 된다

`localized_name` / `localized_description`에 `koreana`를 넣어도 **게임이 지우지 않는다.**
`ModManager.TryMigrateData` → `EnsureLocalizedManifestField`의 IL을 확인한 결과:

- `EnsureLocalizedManifestField`는 `ModManifest.GetUploadL10nIds()`가 주는
  `schinese`/`tchinese`/`english`가 **없을 때만 채워 넣는다.** `Remove` 호출이 없다
- 실제로 지우는 `RemoveLegacyManifestField`는 `workshopId` / `workshop_id` / `priority`
  세 필드에만 쓰인다. 옛 형식의 워크샵 id를 `workshop.json`으로 옮기는 마이그레이션이며,
  우리는 그 필드를 쓰지 않는다
- 즉 `info.json`을 다시 쓰는 건 옮길 게 있을 때뿐이고, 우리 파일은 해당 사항이 없다

표시 시점의 조회는 `ModManifest.get_Title` → `GetL10nText(현재 언어의 SteamL10nId, 폴백, 사전)`
이라 그냥 Dictionary 룩업이다. 그래서 업로더가 편집해주지 않는 키도 표시된다.
값이 빈 문자열이면 `IsNullOrEmpty` 검사에 걸려 최상위 `name`/`description`으로 폴백한다.

실증: 게임이 로컬 폴더를 여러 번 스캔·로드한 뒤에도 `koreana`가 그대로 남아 있었고,
미니맵 모드(`3782262739`)의 **업로드된** 사본에도 `koreana`와 `japanese`가 살아 있다.

### 설치 안내는 항목 페이지에 둔다

`info.json`의 `description`은 한 문장으로 짧게 유지하고("… Requires DTMAPI."),
구독 → Mods 화면에서 활성화 → 재시작 단계는 **Steam 창작마당 항목 페이지 설명**에 적는다
(웹에서 따로 편집한다). `info.json`을 길게 만들지 않으려는 선택이다.
미니맵 모드는 반대로 `localized_description`에 5단계를 다 넣었으니, 그쪽 방식이 필요하면 참고할 것.

## 구독자가 하는 일

1. **DTMAPI**(`3743016467`) 구독 → `1_install_dtmapi.bat` 1회 실행 (BepInEx가 함께 깔린다)
2. 이 항목 구독
3. 타이틀 화면 Mods 페이지에서 **켜기** — 새로 구독한 항목은 꺼진 상태로 등록된다
   (`ModManager.TryLoadMod`이 `enabled=false, priority=-1`로 만든다)
4. 게임 재시작 — DTMAPI는 시작 시점에만 CodeMod을 로드한다
   (`restart Doloc Town to cold-load it`)

1번은 DTMAPI 쪽 사정이고 우리가 줄일 수 없다. 항목 설명에 선행 조건으로 적어야 한다.

## 남은 수작업

`package.ps1`이 만들어주지 못하는 것들:

- [ ] **최초 업로드** — 위 "업로드 절차" 참고.
      발급된 `workshop_id`를 `workshop/workshop.json`에 적어야 이후 갱신이
      같은 항목으로 올라간다
- [ ] **항목 페이지 설명 작성** — 구독 → Mods 화면 활성화 → 재시작 단계.
      `info.json`이 아니라 Steam 쪽 설명에 적는다 (위 "설치 안내는 항목 페이지에 둔다")
