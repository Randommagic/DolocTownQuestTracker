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

## 이 모드의 배포 방식

순수 BepInEx 플러그인이므로 `Content/` 안의 DLL이 **자동으로 로드되지는 않는다.**
구독자는 DLL을 `BepInEx\plugins\`로 복사해야 한다.

DTMAPI의 `CodeMod` 경로(`manifest.json` + `EntryDll` + `EntryType`)는 DTMAPI 전용
진입점 클래스를 요구하므로 해당되지 않는다. 이 모드는 `BepInPlugin` 속성을 가진
BepInEx 플러그인이고, BepInEx가 직접 로드한다.

따라서 `Content/`는 이렇게 구성된다:

```
Content/
├─ DolocTownQuestTracker.dll   플러그인
└─ INSTALL.txt                 복사 위치 안내 (구독자용이므로 영어)
```

## 업로드 절차

게임에 **자체 모드 업로더가 내장**되어 있다. 별도 Steam 도구(SteamCmd, Workshop Uploader)는
필요 없다. `Assembly-CSharp.dll`의 문자열에서 확인한 기능이다.

### 로컬 모드 폴더

게임 설치 폴더가 아니라 유저 데이터 쪽이다.

```
%USERPROFILE%\AppData\LocalLow\RedSawGames\DolocTown\MODS\
```

`package.ps1`이 만든 `dist\workshop\`을 이 아래에 폴더째로 복사한다.

```
MODS\
└─ DolocTownQuestTracker\      ← 이름은 자유
   ├─ info.json
   ├─ workshop.json
   ├─ icon.png
   ├─ preview.png
   └─ Content\
      ├─ DolocTownQuestTracker.dll
      └─ INSTALL.txt
```

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
문자열로 없는데, 설치된 창작마당 항목 4개에는 모두 존재한다. 넣어도 해가 없으니
패키지에 포함하되, **최초 업로드 후 게임이 로컬 폴더에 `workshop.json`을 써주는지
확인할 것.** 써준다면 그 id를 `workshop/workshop.json`에 옮겨 적어야 다음 갱신이
같은 항목으로 올라간다.

### 이 모드 특유의 주의점

순수 BepInEx 플러그인이므로 게임의 모드 목록에서 **켜기/끄기가 실제로 동작하지 않을
가능성이 높다.** BepInEx가 `BepInEx\plugins\`에서 직접 로드하므로 게임의 모드 관리와
무관하다. 창작마당 항목은 파일을 전달하는 수단일 뿐이고, 실제 설치는 구독자가
`INSTALL.txt`대로 DLL을 복사해야 이뤄진다.

## 남은 수작업

`package.ps1`이 만들어주지 못하는 것들:

- [ ] **`icon.png` / `preview.png`** — 직접 만들어 `workshop/`에 넣어야 한다.
      다른 항목들의 크기를 참고할 것
- [ ] **최초 업로드** — 위 "업로드 절차" 참고.
      발급된 `workshop_id`를 `workshop/workshop.json`에 적어야 이후 갱신이
      같은 항목으로 올라간다
- [ ] **설명 문구 다듬기** — `info.json`의 `description`은 초안이다.
      한국어 사용자가 주 대상이면 `localized_description`에 한국어 항목이
      들어가는지 확인할 것 (다른 항목들은 `schinese`/`english`/`tchinese`만 쓴다)

## 전제 조건

BepInEx가 설치되어 있어야 한다. 창작마당의 **DTMAPI** 항목을 구독하면 BepInEx가
함께 설치되므로, 항목 설명에 이를 선행 조건으로 적는 것이 좋다.
