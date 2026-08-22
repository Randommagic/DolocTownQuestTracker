# Doloc Town 모드 — 게임 데이터 조사 결과

> 조사일: 2026-08-20 ~ 08-21
> 대상 빌드: `Assembly-CSharp.dll` 2026-08-18 갱신분 (다른 DLL은 08-17), Unity 2021.3.16f1c1, **Mono 빌드**
> 게임 패치 시 이 문서의 타입·필드가 어긋날 수 있음 — 위 날짜 기준으로 읽을 것

목표: **팩션 미션 + 종자 연구에 필요한 아이템 개수를 게임 화면에 오버레이로 표시하는 BepInEx 모드**

**확정된 방침**: 순수 BepInEx 플러그인 방식 — DTMAPI가 설치한 BepInEx 위에서 동작하되 DTMAPI에 코드 의존하지 않음.
근거는 §3 참조. 미니맵 모드와 동일한 구조.

---

## 1. 조사 환경 / 경로

### 게임 설치
```
C:\Program Files (x86)\Steam\steamapps\common\Doloc Town\
├─ DolocTown.exe
├─ DolocTown_Data\           ← 폴더명에 공백 없음 (주의)
│  ├─ Managed\               ← 참조 DLL 181개
│  └─ StreamingAssets\aa\StandaloneWindows64\
│     └─ configs_assets_all_*.bundle   ← 설정 테이블 (3MB, LZ4)
├─ MonoBleedingEdge\         ← Mono 확정 (IL2CPP 아님 → 디컴파일·Harmony 가능)
│
│  ↓ 2026-08-21 DTMAPI 설치로 추가된 것
├─ winhttp.dll               ← Doorstop 관문 (DLL 하이재킹)
├─ doorstop_config.ini / .doorstop_version / changelog.txt
├─ BepInEx\
│  ├─ core\                  ← ★ 빌드 참조 대상 (18개 파일)
│  │  ├─ BepInEx.dll         5.4.23.5
│  │  ├─ 0Harmony.dll        2.9.0.0
│  │  ├─ BepInEx.Preloader.dll / BepInEx.Harmony.dll / HarmonyXInterop.dll
│  │  └─ Mono.Cecil*.dll / MonoMod.*.dll
│  ├─ plugins\
│  │  ├─ DTMAPI\             DTMAPI 0.6.1 (DLL 5개 + 아이콘)
│  │  └─ <우리 모드가 들어갈 자리>
│  ├─ config\                ConfigEntry가 첫 실행 시 자동 생성
│  └─ LogOutput.log          ← 디버깅 생명줄
└─ DTMAPI\
   ├─ install-state.json
   ├─ logs\latest.log
   └─ backups\bepinex-install-<타임스탬프>\   ← 설치 전 상태 백업
```

기타 경로:
- 세이브/로그: `%LOCALAPPDATA%\..\LocalLow\RedSawGames\DolocTown\` (`SAVE\`, `Player.log`, `MODS\`)
- 워크샵: `steamapps\workshop\content\2285550\`
- 구독 항목: 한글 번역 모드(`3781033909`, 공식 JSON 모드라 참고 코드 없음), DTMAPI(`3743016467`)

### 이 저장소 구조
```
DolocTownQuestTracker/
├─ src/                               플러그인 소스
├─ tools/                             BundleDump.cs, ExtractTables.cs (설정 테이블 추출기)
├─ build.ps1                          빌드 + 게임에 배포
├─ RESEARCH.md                        ← 이 문서
├─ CHECKLIST.md                       남은 확인/작업 목록
└─ PROJECT_CONTEXT.md                 최초 기획 메모
```

### 저장소에 두지 않는 작업물

조사 과정에서 만든 아래 산출물은 **게임 저작물의 디컴파일/추출본**이라 저장소에서 제외했습니다.
필요할 때 아래 방법으로 다시 만들면 됩니다.

| 작업물 | 재생성 방법 |
|---|---|
| 게임 코드 디컴파일 (`Assembly-CSharp`, .cs 약 3,670개) | ILSpy(VS Code 확장)로 `DolocTown_Data\Managed\Assembly-CSharp.dll` export |
| 설정 테이블 195개 JSON | 아래 추출 절차 |
| 레퍼런스 모드 `DolocMinimap.dll` | Workshop `3782262739` 구독 후 ILSpy export |
| 빌드 참조 DLL | 불필요 — `build.ps1`이 설치된 게임 폴더를 직접 참조 |

### 설정 테이블 추출 방법 (게임 패치 후 재현용)
`tools/` 의 C# 두 개를 .NET Framework 컴파일러로 빌드해 실행:

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /out:BundleDump.exe BundleDump.cs
& $csc /nologo /out:ExtractTables.exe ExtractTables.cs

# 1) UnityFS 번들 → 원시 SerializedFile
& .\BundleDump.exe "<게임>\DolocTown_Data\StreamingAssets\aa\StandaloneWindows64\configs_assets_all_*.bundle" configs.raw
# 2) 원시 데이터 → 테이블별 JSON
& .\ExtractTables.exe configs.raw .\config-tables
```

동작 원리 메모:
- 번들 헤더: `UnityFS\0` / version=**8** / `5.x.x` / `2021.3.16f1c1` / size i64 / compressedBlocksInfoSize i32 / uncompressedBlocksInfoSize i32 / flags i32 — 전부 **빅엔디안**
- flags `0x243` = LZ4HC(`0x03`) + BlocksAndDirectoryInfoCombined(`0x40`) + BlockInfoNeedPaddingAtStart(`0x200`)
- **데이터 블록 시작 오프셋은 `fileSize - Σ(블록 압축크기)` 로 역산해야 정확** (헤더 뒤 정렬만으로는 10바이트 어긋남. 실측 614 vs 실제 624)
- 블록 105개, 해제 후 13.7MB
- TextAsset 레이아웃: `[nameLen i32le][name][4바이트 정렬 패딩][dataLen i32le][JSON]` — 여기는 **리틀엔디안**

Python은 이 환경에서 Windows Store 스텁이라 사용 불가. `csc.exe` + PowerShell `Add-Type`은 정상 동작.

---

## 2. 이 게임에서 모드를 만드는 방식

레퍼런스: **DolocMinimap v2.8.0** (Workshop `3782262739`), 총 1,592줄.

### DTMAPI는 필요 없다
```csharp
[BepInPlugin("local.dolocminimap", "Minimap Overlay", "2.8.0")]
public class Plugin : BaseUnityPlugin
```
`[BepInDependency]`가 **없음** → 순수 BepInEx 플러그인. DTMAPI는 BepInEx 인스톨러 역할일 뿐. 인게임 모드 매니저에 안 뜨는 이유도 이것.

### 빌드 설정
```xml
<TargetFramework>net472</TargetFramework>
<GenerateAssemblyInfo>False</GenerateAssemblyInfo>
```

참조 8개:

| 참조 | 위치 | 상태 |
|---|---|---|
| `Assembly-CSharp` | `DolocTown_Data\Managed\` | ✅ |
| `UnityEngine.CoreModule` | 동일 | ✅ |
| `UnityEngine.UI` | 동일 | ✅ |
| `UnityEngine.UIModule` | 동일 | ✅ |
| `UnityEngine.TextRenderingModule` | 동일 | ✅ |
| `Unity.InputSystem` | 동일 | ✅ |
| `BepInEx` | `Doloc Town\BepInEx\core\BepInEx.dll` | ✅ 5.4.23.5 |
| `0Harmony` | `Doloc Town\BepInEx\core\0Harmony.dll` | ✅ 2.9.0.0 |

**참조 8개 전부 확보됨 (2026-08-21).** 설치된 BepInEx/0Harmony 버전이 미니맵 모드가 빌드된 버전과
정확히 일치 — 미니맵의 코드 패턴을 그대로 써도 참조 불일치가 없음.
(미니맵 DLL의 AssemblyRef를 `[Reflection.Assembly]::ReflectionOnlyLoadFrom(...).GetReferencedAssemblies()`
로 읽어 확인: BepInEx 5.4.23.5 / 0Harmony 2.9.0.0 / Unity.InputSystem 1.4.4.0)

ILSpy export라 `HintPath`가 빠져 있음. 코드의 `((BaseUnityPlugin)this).Logger`, `(Object)(object)x != (Object)null`, `LangVersion 14.0`, `AllowUnsafeBlocks`는 전부 디컴파일 산물 — 새로 쓸 땐 평범하게 쓰면 됨.

### 초기화 (`Awake`)
```csharp
Instance = this;                                     // ① static 싱글톤
xxxMethod = typeof(T).GetMethod(..., NonPublic);     // ② 리플렉션 핸들 선캐싱
if (xxx == null) Logger.LogError("Game may have updated.");  //    실패해도 죽지 않음
cfgKey = Config.Bind<Key>("Hotkeys", "...", Key.F9, "설명");  // ③ 설정
Config.Save();
new Harmony(GUID).PatchAll(typeof(Plugin).Assembly); // ④ 패치 일괄 적용
Logger.LogInfo(...);                                 // ⑤
```

### 매 프레임 갱신을 Harmony로 받는다 ← 핵심 패턴
`MonoBehaviour.Update()`를 쓰지 않고 게임 메서드에 Postfix를 걸어 tick을 받음:

```csharp
[HarmonyPatch(typeof(BodyController), "Update")]
internal static class BodyController_Update_Patch {
    private static void Postfix() => Plugin.Instance?.OnTick();
}
```
플레이어가 존재할 때만 돌아감 → 타이틀/로딩 화면에서 자동으로 멈춤.
`OnTick()`은 전체를 try/catch로 감싸 예외를 로그로만 뱉음 (한 프레임 예외로 게임이 죽지 않게).

### 패치는 얇게, 로직은 위임
미니맵 모드의 패치 4개는 모두 13줄이며 `Plugin.Instance`로 위임만 함:

| 패치 대상 | 종류 | 목적 |
|---|---|---|
| `BodyController.Update` | Postfix | tick 펌프 |
| `AgentControllerState.UseItem` | Prefix → `false` | 설정창 열렸을 때 클릭이 게임에 새는 것 차단 |
| `AgentControllerState.UseTool` | Prefix → `false` | 위와 동일 |
| `MapPanel.OnStartShow` | Prefix | 실제 지도 열 때 정리 |

게임 업데이트로 패치가 깨져도 파일 하나만 고치면 됨.

### UI는 uGUI 동적 생성 (`OnGUI` 아님)
```csharp
overlayCanvas = new GameObject("XxxCanvas").AddComponent<Canvas>();
overlayCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
overlayCanvas.sortingOrder = 2000;                       // 게임 UI 위로
Object.DontDestroyOnLoad(overlayCanvas.gameObject);

// 게임의 CanvasScaler 설정을 복사 → 해상도 스케일링 일치
var src = liveMapPanel.GetComponentInParent<CanvasScaler>();
scaler.uiScaleMode        = src.uiScaleMode;
scaler.referenceResolution = src.referenceResolution;
scaler.screenMatchMode    = src.screenMatchMode;
scaler.matchWidthOrHeight = src.matchWidthOrHeight;

gameObject.AddComponent<GraphicRaycaster>();             // 버튼 클릭 받으려면 필수
```

위젯 조립 패턴 (`SettingsPanel.cs`의 팩토리 3개):
```
new GameObject(name, typeof(RectTransform))
  → SetParent(parent, false)
  → anchorMin/Max, pivot, sizeDelta, anchoredPosition
  → AddComponent<Image>() / <Text>() / <Button>()
```

**한글 폰트 필수 처리** — Unity 기본 `Arial.ttf`는 한글이 두부(□):
```csharp
foreach (var t in DolocAPI.uiSystem.GetComponentsInChildren<Text>(true))
    if (t.font != null) return t.font;
Logger.LogWarning("...may render as tofu boxes.");
```

### 설정 저장
`ConfigEntry<T>` → `BepInEx\config\<GUID>.cfg` 자동 생성.
- 키 바인딩: `Config.Bind<Key>(...)` — enum이 `"F9"` 문자열로 저장/파싱
- 창 위치는 픽셀이 아니라 **캔버스 대비 비율**로 저장 (`posX / canvasSize.x`) → 해상도 변경에도 유지

### 입력
새 InputSystem + edge detection 수동:
```csharp
bool now = Keyboard.current[cfgKey.Value].isPressed;
if (now && !prevState) Toggle();
prevState = now;
```

### 방어적 코딩 (개발사가 호환 보장 안 함)
- 리플렉션 실패 → 크래시 대신 `LogError`
- tick 전체 try/catch
- `FullRefreshInterval = 3.0` — 3초마다 참조 재탐색해 씬 전환 후 끊긴 핸들 복구
- `?.` 전면 사용

### 미니맵 고유 트릭 (우리는 안 씀)
Plugin.cs 925줄 중 **약 400줄이 "패널 빌려오기"**: 게임의 실제 지도 패널(`CityMapPanel`)의 부모를 런타임에 바꿔 자기 창으로 끌어오고, 축소·투명도·안개 셰이더 마스크 재바인딩, `RectMask2D`/`Mask`/`ScrollRect` 비활성화, 반납 시 원본 상태 15개 필드 복원. 텍스트를 새로 그리는 우리 모드에는 불필요.

---

## 3. DTMAPI — 조사 결과와 방식 결정

조사일 2026-08-21. DTMAPI **0.6.1** (BinaryVersion 0.6.1.0, 어셈블리 버전은 0.5.3.0, BuildCommit `db5e518a6d7f`).
Workshop `3743016467`. 설치 완료, 상태 `INSTALLED_HEALTHY`.

### 결론: (A) 순수 BepInEx 플러그인으로 간다

| | 가능한가 | 이유 |
|---|---|---|
| **(A) 순수 BepInEx 플러그인** | ✅ **채택** | 미니맵 모드와 동일. 게임 어셈블리 전체 접근 가능 |
| (B) DTMAPI CodeMod | ❌ 불가 | 게임 데이터 접근이 폐쇄 허용목록에 막힘 (아래) |

### DTMAPI가 제공하는 것 (참고용 — 우리는 안 씀)

SMAPI를 모델로 한 프레임워크. `DTMAPI.Abstractions`에 public 타입 **270개**.

```csharp
public abstract class DtmMod {                 // 모드 진입점 (SMAPI의 Mod과 동일)
    public IManifest Manifest { get; }
    public IMonitor  Monitor  { get; }
    public abstract void Entry(IDtmHelper helper);
}
```
`IDtmHelper` 표면: `Config` / `Content` / `Diagnostics` / `Events` / `Input` / `ModManifest` /
`ModRegistry` / `Monitor` / `Translation` / `UI` / `Workshop` + `ReadConfig<T>()` / `WriteConfig<T>()`

- `IDtmConfigMenuApi` — 완성도 높은 설정 메뉴 빌더. `AddBoolOption` / `AddNumberOption(min,max,interval)` /
  `AddKeybindOption` / `AddChoiceOption` / `AddColorPresetOption` / `AddSectionTitle` / `AddParagraph` /
  `AddButton` / `Register(mod, reset, save, titleScreenOnly)` / `GetKeybindConflicts`
- `IGameLoopEvents` — `UpdateTicked`, `OneSecondUpdateTicked`, `GameLaunched`, `ReturnedToTitle` (Harmony 없이 tick)
- `IInputEvents` / `ISaveEvents` / `IUiEvents` / `IWorkshopEvents` / `IDiagnosticsEvents`
- `IInputHelper` — `RegisterKeybind`, `WasPressed`, `IsKeybindDown`, `Suppress`
- `IContentQueryHelper` / `IContentItemInfo`, `ITranslationHelper`, `IMonitor`

어셈블리 구성: `DTMAPI.Abstractions`(계약, netstandard2.0 무의존) → `DTMAPI.Core` → `DTMAPI.GameBridge.DolocTown` /
`DTMAPI.ModConfigMenu` / `DTMAPI.BepInExBootstrap`(BepInEx 참조하는 실제 플러그인)

### (B)를 못 쓰는 이유 — 근거 3개

**1) 우리에게 필요한 API가 없다.** `*Api` 인터페이스 33개를 전수 확인:
```
IActionCompletionApi IActionSpeedApi IAdvancedDebugApi IAnimalViewerApi IAudioReplacementApi
ICameraViewApi ICameraZoomApi IChestLocatorEnhancerApi ICropHarvestingApi ICustomAnimalApi
ICustomAttackApi ICustomDroneApi ICustomMonsterApi IDebugConsoleApi IDtmConfigMenuApi
IDtmConfigMenuKeybindDefaultsApi IDtmDiagnosticsApi IEquipmentSlotsApi IFishingAutomationApi
IInstantSaveDebugApi IInventoryDebugApi IItemDisplayNameApi IItemTooltipApi ILampControlApi
IMachineProductionApi IMailDeliveryApi IMovementDebugApi IPanoramaCameraApi ISaveSlotsApi
IStrongPlantingGunApi ITeleportDebugApi ITimeDebugApi IWeatherDebugApi
```
**미션·팩션·종자·퀘스트 관련 API가 하나도 없음.** `Mission|Faction|Seed|Quest|Research|Unlock|Submit`
패턴으로 타입 전체를 검색해도 무관한 것만 걸림 → `Assembly-CSharp` 직접 참조가 불가피.

**2) 게임 어셈블리 직접 참조는 폐쇄 허용목록.** CodeMod 등급은
`CodeMod` / `StrictCodeMod` / `AdvancedCodeMod` / `LegacyNativeCompatibilityCodeMod`.
`DTMAPI.Core.dll` **내장 리소스**에 Advanced 참조 정책이 하드코딩돼 있음:
```
DTMAPI.Core.AdvancedReferencePolicyRegistry.json
DTMAPI.Core.AdvancedReferencePolicy.doloctown-23762374-{actionspeed,animalhusbandryprogress,
    chestlocator,fishbreedingassistant,g2,mine,oneactioncomplete,strongplantinggun,zoom}-v1.json
DTMAPI.Core.AdvancedReferencePolicy.doloctown-24456188-{autofishing,debugconsole,
    moreequipmentslots,moresaves}-v1.json
```
작성자 ID 2개(`23762374`, `24456188`)의 모드 **14개만** 허용. 각 정책이 참조를 이렇게 못박음:
```json
"references": [
  { "gameRelativePath": "BepInEx/core/0Harmony.dll", "assemblyName": "0Harmony", "length": 204800 },
  { "gameRelativePath": "DolocTown_Data/Managed/Assembly-CSharp.dll",
    "assemblyName": "Assembly-CSharp", "length": 5993984 }
]
```
관련 오류 문자열: `strict-native-reference-forbidden`,
`Receipt referencePolicyId is not present in the Runtime-embedded exact registry`,
`Advanced native AssemblyRefs must exactly match the tracked receipt policy`,
`Advanced Workshop CodeMods require a native-verified subscription/install root`.
→ `dtmapi-advanced-references.json`을 직접 써넣어도 런타임 내장 레지스트리에 없으면 검증 실패.
**제3자가 자력으로 열 수 없음.**

**3) Author SDK가 미공개.** `install-to-game.ps1`에 명시:
> New managed product installation is paused for DTMAPI 0.6.1 because the **unpublished Author SDK**
> still targets the retired `<game>/Mods` root. Install Runtime only, or use the official MODS release transaction.

### 부수 제약 (B를 택했다면 걸렸을 것들)
- `CodeMod entry DLL TargetFramework must be exactly netstandard2.0` — net472 불가
- `Canonical Harmony owner must use the non-empty dtmapi.mod.<uniqueid> namespace` — Harmony ID 강제
- 매니페스트 위치: `Content/DTMAPI/dtmapi-package.json` (게임 공식 MODS 구조 안)
- 순수 BepInEx 플러그인이 DTMAPI 설정 메뉴에 접근할 **public static 진입점이 없음**
  (`DTMAPI.Core`의 public static은 `DtmOverlayPage`, `DtmApiRuntime`, `ManagedModIdentity` 뿐 — 서비스 로케이터 아님)

### (A)로 잃는 것 = 거의 없음

| DTMAPI가 주는 것 | (A)에서의 대체 | 실질 비용 |
|---|---|---|
| `IDtmConfigMenuApi` | 자체 설정창 | 미니맵 `SettingsPanel.cs`에서 복사 — 낮음 |
| `GameLoop.UpdateTicked` | `BodyController.Update` Harmony Postfix | 없음 |
| `IInputHelper` | `Keyboard.current[key]` + edge detection | 없음 |
| `IMonitor` | BepInEx `Logger` | 없음 |
| `IConfigHelper` | `ConfigEntry<T>` | 없음 |
| `IContentQueryHelper` | `DolocConfig.Tables` 직접 | (A)가 더 강력 |
| 인게임 설정 페이지 통합 | F9 별도 창 | UX 일관성만 손실 |

### 기준선 로그 (2026-08-21, DTMAPI 단독 / 게임 실행 + 세이브 로드 + 종료)

`BepInEx\LogOutput.log` 120KB. 우리 플러그인 투입 후 비교할 기준선.

```
BepInEx 5.4.23.5 — DolocTown
Unity v2021.3.16.5692413 / CLR 4.0.30319.42000
Supports SRE: True            ← Harmony 동적 패치 가능
System platform: Bits64, Windows
Preloader started → Chainloader ready → 1 plugin to load
Loading [DTMAPI Bootstrap 0.6.1.0] → Chainloader startup complete
```

| 항목 | 결과 |
|---|---|
| Error / Warning / Fatal | **0 / 0 / 0** |
| DTMAPI 기능 | 14개 전부 `ready`, `failedFeatures=0`, `autoDisabledFeatures=0` |
| 등록 API | 29개 (`kinds=Api:registered=29`) |
| 세이브 로드 | `requests=1`, `timeouts=0`, `failures=0` |
| 이벤트 디스패치 | 2,589건, `failures=0` |
| 리소스 원장 | `status=ok`, `warnings=0`, `errors=0` |
| 종료 | `SaveLoadBoundary = closed`, `pending=0` |

활성 기능 14개: ActionCompletion, ActionSpeed, AnimalViewer, AudioReplacement, Camera,
ChestLocatorEnhancer, CropHarvesting, CustomAnimalAnimatorBridge, EquipmentSlots,
FishingAutomationCompatibility, FishRoeTooltip, ItemDisplayName, NativeUiLayoutDiagnostics, SaveSlots

**첫 실행으로 생성된 것**: `BepInEx\config\BepInEx.cfg`, `BepInEx\patchers\`, `BepInEx\cache\`,
`DTMAPI\logs\latest.log`
→ 개발 중 실시간 로그를 보려면 `BepInEx.cfg`의 `[Logging.Console] Enabled = true`

### ✅ 패치 충돌 우려 — 실측 결과 문제없음

**1) 입력 억제 충돌: 해당 없음**
```
input={status=ok; owners=0; buttons=0; ownerRegistrations=0; byOwner={none}}
```
DTMAPI 입력 억제 시스템의 소유자가 0 = 유휴. `Keyboard.current` 직접 읽기에 간섭 주체 없음.

**2) `AgentControllerState` 패치 겹침: 현재 없음**
`AgentControllerState`가 로그에 **한 번도 등장하지 않음.** DTMAPI가 실제로 Harmony 패치한 것으로
확인되는 건 `LoadGame`(Prefix + Postfix)뿐:
```
source=Harmony LoadGame Prefix
source=Harmony LoadGame Postfix
```
DLL 문자열에 있던 `AgentControllerStateUseItemContinuesPrefix`는 ActionSpeed / ActionCompletion
기능 소속인데, 두 기능이 `ready`여도 패치 로그가 없음 → **소비자(Advanced 모드)가 없으면 패치를
걸지 않는 지연 적용 구조**로 보임. `FishingAutomationCompatibility`의
`compatibilityStatus=inactive/no-consumer`가 같은 패턴을 명시.

⚠ 단 이는 **"소비자 없는 상태"의 관측**. 사용자가 ActionSpeed 계열 Advanced 모드를 켜면 겹칠 수 있음.
우리 Prefix가 원본을 취소하는 건 설정창이 열린 동안뿐이라 실질 위험은 낮지만 재확인 지점으로 남김.

### ⚠ 참고 — DTMAPI는 침습적이다 (정적 분석에서 확인한 표면)

우리 플러그인과 **같은 타입을 패치**하고 Unity PlayerLoop을 조작함:
```
AgentControllerStateUseItemContinuesPrefix / AgentControllerStateInteractContinuesPrefix
AgentControllerState.UseItemContinues.AdjustActionSpeedDelta
DolocTown.AgentControllerState / DolocTown.DolocUserInput / DolocTown.NormalGameState
GetCurrentPlayerLoop / OnPlayerLoopUpdate / playerLoopCallbacks=
Owner-root suppression / unexpectedRemainingSuppressedRoots=
DTMAPI Input System frame driver / fallback health pump
```
우리 계획은 `AgentControllerState.UseItem/UseTool`에 Prefix를 걸어 `false`로 원본을 취소하는 것이라
잠재적으로 대상이 겹침. → **위 기준선 실측 결과 현재는 충돌 없음** (패치가 지연 적용됨).
미니맵 모드도 정확히 이 패치를 갖고 DTMAPI 환경에서 쓰이고 있음.

### 관찰 사항 (미검증 — 단정 금지)
Advanced 참조 정책이 고정한 `Assembly-CSharp.dll` 길이는 **5,993,984 / 6,384,128**인데
현재 설치본은 **6,398,976** (2026-08-18 패치). 작성자 본인의 Advanced 모드들이 현재 빌드에서
참조 검증을 통과하지 못하는 것처럼 보임. 단 이는 내장 정책 JSON을 정적 분석한 추론이며,
실제로 Advanced 모드를 돌려 실패를 확인한 것은 아님. 길이/해시 검증이 다른 경로로 갱신될 수도 있음.

### 설치 절차 (재현용)
```powershell
# 게임을 먼저 완전히 종료할 것 (실행 중이면 파일 잠김으로 설치 실패/불완전)
$env:DTMAPI_NO_PAUSE = '1'    # dispatcher가 여러 지점에서 pause를 호출 → 비대화형 실행 시 필수
& "<workshop>\3743016467\1_install_dtmapi.bat"
& "<workshop>\3743016467\3_check_dtmapi_status.bat"   # INSTALLED_HEALTHY 확인
# 되돌리기: 2_uninstall_dtmapi.bat  (Runtime만 제거, 콘텐츠 팩은 보존)
# 로그 수집: 4_collect_dtmapi_logs.bat → Desktop\DTMAPI-logs
```
설치 시 `Package payload install detected; leaving existing game Mods folders untouched.`
→ 기존 한글 번역 모드는 건드리지 않음. 설치 전 상태는 `DTMAPI\backups\`에 백업됨.

### 향후 재검토 트리거
(B)를 다시 볼 값이 생기는 조건 — 그 전엔 (A) 유지:
- Author SDK 공개 + 제3자 Advanced 참조 정책 등재 절차 제공
- 또는 DTMAPI가 미션/팩션/종자 GameBridge API를 추가

옮기게 되더라도 데이터 읽기 로직은 그대로 두고 진입점 껍데기(`BaseUnityPlugin` → `DtmMod`)만
교체하는 수준이므로, 지금 (A)로 진행해도 매몰비용이 크지 않음.

---

## 4. 데이터 계층 — 두 층

| | 정적 정의 (게임 규칙) | 런타임 상태 (내 세이브) |
|---|---|---|
| 진입점 | `DolocConfig.Tables` | `DolocAPI.archiveHandle` |
| 내용 | "이 미션은 감자 5개 필요" | "이미 냈는지 / 지금 몇 개 보유" |
| 규모 | **테이블 195개** | 세이브 트리 5분기 |
| 형식 | Luban(`Bright.Config`) 생성, Addressables `Configs/<name>.json` 로드 | Newtonsoft JSON |

### 정적 테이블 공통 인터페이스
```csharp
DolocConfig.Tables.TbXxx.GetOrDefault(id)   // 없으면 null
DolocConfig.Tables.TbXxx.Get(id)            // 없으면 예외
DolocConfig.Tables.TbXxx.DataList           // List<XxxInfo>
DolocConfig.Tables.TbXxx.DataMap            // Dictionary<string, XxxInfo>
```

주요 분류 (195개 중):

| 영역 | 테이블 |
|---|---|
| 미션 | `TbMission`, `TbMissionType`, `TbMissionDecorator`, `TbBoardMission`, `TbBoardMissionLevel/Type`, `TbFactionMission`, `TbFactionMissionType`, `TbFactionType`, `TbItemSubmitCondition`, `TbItemGeneCondition`, `TbRewardPool`, `TbCustomEvent`, `TbPositionType`, `TbMissionItem`, `TbTreatyPortFaction` |
| 아이템 | `TbItem`, `TbItemMainType`, `TbItemSubType`, `TbItemSource`, `TbItemOrder`, `TbEatingEffect`, `TbCountItemList`, `TbRangedItemList` |
| NPC | `TbNpc`, `TbNpcDocument`, `TbNpcLiking`, `TbLikingLevelMap`, `TbIdleTalkNode`, `TbTrashTalk` |
| 제작 | `TbRecipe`, `TbRecipeGroup/MainType/SubType`, `TbIngredientGroup`, `TbDish`, `TbDishGroup`, `TbDismantleRecipe` |
| 농사 | `TbSeed`, `TbSeedType`, **`TbSeedUnlock`**, `TbTreeSeed`, `TbCropGene`, `TbCropGeneMatrix` |
| 맵 | `TbScene`, `TbMapType`, `TbMapRoom`, `TbMapArea`, `TbRoom`, `TbMarkPoint`, `TbPortal`, `TbStation` |
| 생물 | `TbFish`, `TbFishingPool`, `TbFarmFish`, `TbAnimal`, `TbFeed`, `TbHusbandry`, `TbMonster` |
| 건축 | `TbBuilding`, `TbBuildingExterior/Wallpaper`, `TbPlatform`, `TbEquipment`, `TbRoomEffect` |
| 상점 | `TbStore`, `TbStoreItemList`, `TbExchangeStore`, `TbStoreItemUnlock` |
| 시간/환경 | `TbCalendar`, `TbSeason`, `TbWeather`, `TbDayPeriod`, `TbFestival`, `TbTimer` |
| 기술/드론 | `TbTechTree`, `TbTechNode`, `TbTechPoint`, `TbDrone*`, `TbEnvOptimizer*` |
| 도감 | `TbChipDocument`, `TbPlantDocument`, `TbCharacterDocument`, `TbFishDocument`, `TbMonsterDocument` |
| 로컬라이제이션 | `TbTextMapperKO/EN/JA/ZH_CN/ZH_TW/DE/FR/RU/PT_BR`, `TbLocalization`, `TbStaticText`, `TbL10nText/Image/Link`, `TbTextTip` |
| 모드 확장 | `TbMod*Extension` 9개 (공식 JSON 모딩용 훅) |

### 세이브 트리
```
DolocAPI.archiveHandle  (ArchiveDataHandle)
├─ farmData (FarmArchiveData)
│  ├─ agentData          체력/에너지/정신력(+최대치·오버플로우), 돈, 배낭Lv, 농장Lv,
│  │                     플레이어명, 생일, 대시·2단점프 해금, buffManager, 장비, 위치
│  ├─ inventory          InventorySystem
│  ├─ missionManager     스토리/게시판 미션 (IMission)
│  ├─ missionChainManager  체인 진행/완료
│  ├─ emailManager / mapManager / recipeManager / collectionManager
│  ├─ envOptimizerSystem / indieFarmManager / techLevelManager
│  └─ unlockedTechTree, unlockedTechNodes, **unlockedSeedNodes**, **seedsCanUnlock**,
│     unlockedBuildings, unlockedGeneNames, unlockedTutorials  (HashSet<string>)
├─ cityData (CityArchiveData)
│  ├─ **factionMissionManager**    ★ 팩션 미션
│  ├─ boardMissionManager          게시판 미션 풀
│  ├─ npcManager / likingManager / storeManager / documentManager
│  ├─ dialogueManager / calendarManager / cdManager
│  └─ treatyPortFactionManager / gateManager / cityRooms
├─ timeData              totalSeconds, dateNow(DateInfo), TotalDays, climateManager
├─ dungeonData           던전 진행, trainFlag
└─ extraData.achievementSystem
```

### `DolocAPI` 정적 바로가기 (트리 안 타고)
```csharp
DolocAPI.IsNormalState        // 정상 플레이 중? (메뉴/컷신 아님) — tick 가드 필수
DolocAPI.agent               // BodyController
DolocAPI.AgentPosition / AgentRoomCellPosition / AgentWorldCellPosition
DolocAPI.CurrentRoom
DolocAPI.SelectedItem / SelectedItemIndex / SelectedEquipment / SelectedBuilding
DolocAPI.uiSystem            // DolocUiSystem — GetEntity<T>(owner)
DolocAPI.UserInput           // DisableAllInput(true) / ResumeCurrentInput()
DolocAPI.archiveHandle.CurrentMoney
DolocAPI.archiveHandle.CurrentHealthPercent / CurrentEnergyPercent / CurrentSpiritPercent
DolocAPI.archiveHandle.DateNow / DayProcess / CurrentWeekDay / LocalWeatherType
DolocAPI.GetNpcTitle(npcId, ignoreUnknown)
DolocAPI.CurrentL10nId       // "ko" 등
DolocAPI.Broadcast(GameEventType, GameEventArgs)   // 전체 이벤트 허브
```

---

## 5. 미션 시스템 — 3종류

| 종류 | 정적 | 런타임 | 성격 |
|---|---|---|---|
| 스토리 | `TbMission` | `Mission : IMission` | 노드 그래프 진행형 |
| 게시판 | `TbBoardMission` | `MissionWithBoard : IMission` | 시간제한, 랜덤 풀 |
| **팩션** | **`TbFactionMission`** | **`FactionMission`** (IMission 아님) | **아이템 제출형** ← 우리 대상 |

`IMission` 구현체는 `Mission`과 `MissionWithBoard` 둘뿐. `BoardMission`/`FactionMission`은 별개 데이터 홀더.

### `IMission` 표면 (스토리/게시판용, 참고)
```csharp
m.Id / m.Title / m.Description / m.Sender / m.Tip
m.BriefStatus          // "3/5" — 리치텍스트 색 태그 포함 (.ClearRichTextLabel() 로 제거)
m.BriefStatusList      // 복수 조건이면 ["3/5", "0/1"]
m.IsComplete / m.HasTimeLimit / m.LeftTime
m.Rewards / m.MissionLogs / m.BaseInfo / m.ContentHandle
```
진행 카운터 자체는 `MissionRequire_Simple.Handle.count` **private 필드** — 정수 원본이 필요하면 리플렉션. 다만 `ToString()`이 `"{count}/{total}"`을 반환하고 그게 `BriefStatus`로 public 노출됨.

표시용 DTO도 있음: `new MissionData(mission, isChainComplete)` → `tipWithStatus`("감자를 가져다주세요 3/5"), `typeIcon`, `previewImage`, `timeLimit`, `rewardData`, `mapTipData`(`worldPositions`, `roomIds`, `tipTypes`).

### 목표 종류: `GameEventType` 92개
```
SUBMIT_ITEM, OBTAIN_ITEM, GIFT_ITEM, MAKE_ITEM, SELL_ITEM_IN_STORE, USE_ITEM, DROP_ITEM
HARVEST_CROP, PLANT_CROP, WATER_CROP, FERTILIZE_CROP, PLANT_TREE, CLEAR_CROP
SLAIN_MONSTER, FELL_DUNGEON_RESOURCE, OPEN_TREASURE_BOX, HURT_BY_MONSTER
TALK_WITH_NPC, VISIT_NPC_NAME, COMPLETE_DIALOGUE, NPC_LIKING_RISE, NPC_LIKING_MAX_NEW
FISHING_CATCH_FISH, ANIMAL_PRODUCE, ANIMAL_BIRTH, SYNTHESIZE_CROP_GENE
BUILD_BUILDING, BUILD_EQUIPMENT, UNLOCK_TECH_NODE, UNLOCK_SEED
ARRIVE_ROOM_CITY/DUNGEON/BUILDING/FARM, MAKE_MONEY, DAY_PASSED, WEATHER_CHANGED, ...
```

### 게시판 미션 48개 — 우리 대상 아님
`content: {event_type, args, count}` 단일 조건.

| event_type | 건수 | label |
|---|---|---|
| `COMPLETE_DIALOGUE` | 36 | collection |
| `SLAIN_MONSTER` | 6 | battle |
| `FELL_DUNGEON_RESOURCE` | 6 | gather |

`COMPLETE_DIALOGUE` 36건은 label이 `collection`인데 이벤트는 대화 완료 → **실제 요구 아이템이 대화 노드 그래프 안에 숨어 있어 정적 추출 불가.** `mission_tbmission.json`(159KB)을 파야 함. **아직 미조사.**

### `TbItemSubmitCondition` 13개 — 필터 방식
전부 `submit_count: 1`. 개수가 아니라 조건을 정의:
```json
{ "id": "evernight_newyear_bakefood", "submit_count": 1,
  "item_sub_type_filter": ["food_baking"] }          // "베이킹 음식 아무거나 1개"
{ "id": "gene_seed_multiple", "submit_count": 1,
  "item_sub_type_filter": ["farm_seed"],
  "gene_condition": "gene_multiple" }                 // "다중 유전자 씨앗 1개"
```
`ItemSubmitConditionInfo.CheckCondition(Item)`이 public 판정 메서드.

---

## 6. 팩션 미션 — 실제 데이터

**총 62개.** 61개가 아이템 제출형, 9개가 돈 요구형(일부 중복).

### 원본 형태
```json
{
  "id": "vulture_mineral",
  "main_series": 2, "sub_series": 2, "order_in_type": 3,
  "default_unlock": false, "unlock_before_settled": true,
  "title":       { "key": "fation_mission_vulture_mineral", "text": "..." },
  "sender":      { "key": "faction_mission_vulture_mineral_content", "text": "..." },
  "description": { "key": "faction_mission_vulture_mineral_desc", "text": "..." },
  "reputation_value": 10,
  "required_items": [
    { "item_name": "copper_ore", "item_count": 3 },
    { "item_name": "iron_ore",   "item_count": 3 },
    { "item_name": "gold_ore",   "item_count": 3 }
  ],
  "required_money": 0,
  "extra_rewards": [ { "reward_type": 6, "target_id": "...", "target_count": 0 } ]
}
```

### 분포
| 세력 (`main_series`) | 건수 | | 분류 (`sub_series`) | 건수 |
|---|---:|---|---|---:|
| 돌록 (1) | 15 | | 자원 교환 (1) | 21 |
| 콘티키 (3) | 13 | | 기초 건설 (5) | 11 |
| 세로 리코 (5) | 11 | | 산업 지원 | 7 |
| 벌처 성채 (2) | 10 | | 생물학적 탐사 | 7 |
| 텡그리의 아이 (4) | 10 | | 풍토 탐방 (7) | 7 |
| 구도시 폐허 | 3 | | 지질학적 탐사 (2) | 3 |
| | | | 토양 복구 / 천상의 시련 | 3 / 3 |

`sub_series` 원문 키: `materials`(자원 교환), `geological`(지질학적 탐사), `mechanical`(造物研究), `trade`, `traffic`(기초 건설), `tiological`(풍토 탐방)

### 예시
```
[vulture_mineral]  돌록 일반 광물 연구     벌처 성채 / 지질학적 탐사, 명성 10
  구리 광석 x3 (copper_ore) / 철 광석 x3 (iron_ore) / 금 광석 x3 (gold_ore)

[vulture_fossil]   돌록의 특수 광물 연구   명성 10
  녹주석 x1 / 호박 x1 / 고기 모양 돌 x1 / 흑요석 x1

[vulture_spmineral] 돌록의 화석 연구       명성 10
  삼엽충 화석 x1 / 암모나이트 화석 x1 / 양치식물 화석 x1

[kontiki_basic_fish] 기초 어류 연구        콘티키 / 생물학적 탐사, 명성 10
  잉어 x1 / 메기 x1 / 초어 x1 / 정어리 x1 / 배스 x1

[doloc_plantresearch] 돌록 계곡 식물 조사  돌록 / 풍토 탐방, 명성 5, 의뢰인 러비어
  대나무통 x10 (bamboo_tube) / 가시 x10 (thorn)

[doloc_electricity]  전력 지원
  배터리 x10 / 전선 x20 / 오래된 엔진 코어 x1

[cerro_rico_cellar_upgrade3] 공간 극대화   세로 리코 / 자원 교환
  고춧가루 x20 / 민트 오일 x20 / 코코아 파우더 x20 / 티백 x20 / 분쇄 원두 x20

[doloc_kasiafood]    과거의 맛  ← 아이템 10종 (최다)
  블루베리·왁스 애플·수박·딸기·포도·크롤러스·썬더그래스·고추·파·민트 잎 각 x1

[doloc_trading_port] 상무부 수리 자금 모집  ← 아이템 없이 돈만
  required_items: [], required_money: 5000
```

가장 많이 요구되는 아이템: `stone`(5) > `titanium_ingot`/`steel_ingot`/`scrap_engine`/`plastic`/`metal_framework`/`endyam`(각 3) — **모든 미션에 공통으로 들어가는 아이템은 없음.**

---

## 7. 종자 연구 — 실제 데이터

**`plant_tbseedunlock`, 18종.** 모든 항목에 `plant_research_report`(식물 연구 보고서)가 **예외 없이** 들어가고, 여기에 작물/재료가 추가로 붙는다. 195개 테이블 중 `"costs"` 필드를 쓰는 유일한 테이블.

```json
{
  "id": "seed_tomato",
  "costs": [
    { "item_name": "plant_research_report", "item_count": 50 },
    { "item_name": "thorn",                 "item_count": 30 },
    { "item_name": "potato",                "item_count": 30 }
  ],
  "email_id": "seed_tomato",
  "default_unlock": true
}
```

| 종자 | 연구 보고서 | + 추가 재료 | 기본해금 |
|---|---:|---|:-:|
| `seed_tomato` | **50** | 가시 x30 + 감자 x30 | ✅ |
| `seed_chanterelle` | 35 | 수지 x10 | ✅ |
| `seed_beer_flower` | 25 | 대나무통 x10 | ✅ |
| `seed_coffee` | 20 | 나무 x50 | ✅ |
| `seed_tea` | 20 | 나무 x50 | ✅ |
| `seed_potato` | 15 | 섬유 x50 | ✅ |
| `seed_watermelon` | 15 | 토마토 x5 | ❌ |
| `seed_mint` | 10 | 켈프 x10 | ❌ |
| `seed_scallion` | 10 | 가시 x10 | ❌ |
| `seed_turnip` | 10 | 배추 x10 | ❌ |
| `seed_pepper` | 10 | 커피콩 x5 + 찻잎 x5 | ❌ |
| `seed_grape` | 10 | 홉 x15 | ❌ |
| `seed_mushroom` | 10 | 진홍 자낭균 x10 + 꾀꼬리버섯 x10 | ❌ |
| `seed_corn` | 5 | 벼 x10 + 밀 x10 | ❌ |
| `seed_pumpkin` | 5 | 가시 x30 | ❌ |
| `seed_cucumber` | 5 | 록우드 x30 | ❌ |
| `seed_strawberry` | 5 | 블루베리 x20 | ❌ |
| `seed_chinese_cabbage` | 3 | 섬유 x20 | ✅ |

### 연구 보고서 획득 경로
`recipe_tbrecipe`에 **작물 1개 → 보고서 N개** 레시피 29종 (`show_in_handbook: false`이라 도감 비노출):

| 투입 | 산출 | 시간 |
|---|---|---|
| 아가베 x1 | 33~34 | 60h |
| 에덴 꽃 x1 | 25~28 | 48h |
| 호박 x1 | 11 | 24h |
| 수박 x1 | 11~12 | 24h |
| 순무 x1 | 6 | 12h |
| 코코아 x1 | 5~6 | 12h |
| 배추 x1 | 4 | 7h |
| 옥수수 x1 | 3 | 5h |
| 토마토 x1 | 1~2 | 3h |
| 감자 x1 | 1 | 1h |

`min_count != max_count`인 것은 확률 산출.

`plant_research_report`가 18종 전부의 공통 병목 → 오버레이에서 별도 강조 후보.

### 기술 트리는 아이템을 안 씀
`techtree_tbtechpoint`는 `{level, required_exp, reward_points}` (Lv0=10exp, Lv1=30, Lv2=60...). `techtree_tbtechnode`는 제목/설명만, 비용 필드 없음. `envoptimizer_tb*`(총 6KB)도 아이템 비용 없음.
→ **"연구에 아이템 제출"은 기술 트리가 아니라 팩션 미션 연구 계열 + 종자 연구.**

---

## 8. ★ 목표 오버레이 접점 map

### 핵심 발견: 두 시스템의 모델이 동일하다
둘 다 `CountItem[] 필요목록` + `완료 플래그`로 환원된다.

**완료 여부는 줄 단위로 기록·영속된다.** `FactionItem.FinishState`가 `[JsonProperty]`로 세이브에 저장되고, 재제출은 이 플래그가 막는다 (인게임에서 이미 낸 항목이 제출 불가로 보이는 이유):

```csharp
public bool CanSubmitFactionMissionItem(string itemName, out int targetCount) {
    for (int i = 0; i < FactionItems.Length; i++)
        if (FactionItems[i].ItemName == itemName && !FactionItems[i].FinishState) {
            targetCount = FactionItems[i].Count;
            return true;
        }
    targetCount = int.MaxValue;
    return false;   // → UI가 "제출 불가" 에러 박스
}
```

**반면 한 줄 내부의 부분 누적은 존재하지 않는다** — 게임 규칙 자체가 전량 일괄 제출이기 때문:

```csharp
// FactionMissionUiState.CheckSingleItem — 전량 보유해야 통과
return DolocAPI.CountItem(item.name, checkBox: true) >= targetCount;

// 통과 시
int submitCount = currentMission.MaxSubmitFactionMissionItemCount(name);  // 필요량 전체
ShowQuestionBox("{submitCount}개 {title} 제출?", () => {
    inventorySystem.CostAt(index, submitCount, useBox: true);   // 전량 한 번에 차감
    missionMgr.SubmitFactionMissionItem(currentMission.Id, name);  // FinishState = true
});
```

구리 광석 3개 중 1개만 먼저 내는 것은 불가능하다. `FactionItem.Count`는 필요량 고정값이며 제출해도 줄지 않는다.
종자 연구도 동일하게 전액 일괄 지불(`unlockedSeedNodes` HashSet에 추가될 뿐).

→ **각 줄의 상태는 3가지**, 전부 public API로 판정 가능:

| 상태 | 판정 |
|---|---|
| 완료 | `FinishState == true` |
| 제출 가능 | `!FinishState && CountItem(name, true) >= Count` |
| 부족 | `!FinishState && CountItem(name, true) < Count` — 부족분 = `Count - 보유` |

표시할 숫자는 `보유/필요` 하나로 통일된다. 단 이 값은 **모드가 인벤토리와 비교해 계산한 것**이고, 게임이 들고 있는 상태가 아니다.

### 공통 원소 타입
```csharp
public struct CountItem {          // namespace DolocTown
    public string itemName;
    public int itemCount;
    public bool isValid { get; }
}
```
`FactionMissionInfo.RequiredItems`(List)와 `SeedUnlockInfo.Costs`(배열)가 같은 타입 → **UI 렌더링 코드 공유 가능.**

### (1) 정적 — 필요 개수
```csharp
DolocConfig.Tables.TbFactionMission             → FactionMissionInfo
    .Id / .MainSeries / .SubSeries / .OrderInType
    .Title / .Sender / .Description             // ★ 이미 현재 언어로 치환됨 (조인 불필요)
    .ReputationValue
    .RequiredItems : List<CountItem>            // ★★
    .RequiredMoney : int
    .ExtraRewards  : List<RewardProto>
    .DefaultUnlock / .UnlockBeforeSettled

DolocConfig.Tables.TbFactionType.GetOrDefault(...).Title           // 세력명
DolocConfig.Tables.TbFactionMissionType.GetOrDefault(...).Title    // 분류명

DolocConfig.Tables.TbSeedUnlock                 → SeedUnlockInfo
    .Id            // "seed_tomato" — 씨앗 아이템 ID 겸 노드 ID
    .Id_Ref        : ItemInfo
    .Costs         : CountItem[]                // ★★
    .EmailId / .DefaultUnlock
```

### (2) 런타임 — 완료 여부
```csharp
// 팩션
var fmm = DolocAPI.archiveHandle.cityData.factionMissionManager;
fmm.TotalMissions                               // FactionMission[]  ← 해금된 것만!
fmm.GetFactionMissions(mainSeries, subSeries)   // 정렬됨: 미완료 먼저, OrderInType desc
fmm.GetFactionMissionSubTypes(mainSeries)
fmm.QueryFactionMission(id, out mission)

m.Proto           : FactionMissionInfo
m.FactionItems    : FactionItem[]
     .ItemName / .Count(필요량, 불변) / .FinishState(bool)
m.MoneyFinishState / m.IsComplete / m.FamePoints / m.Rewards
m.CanSubmitFactionMissionItem(itemName, out targetCount)

// 종자 — HashSet 직접 접근 대신 확장 메서드 (ArchiveOperationFarm, DolocTown.GameData)
var h = DolocAPI.archiveHandle;
h.IsSeedNodeUnlocked(id)            // 이미 해금
h.IsSeedNodeAvailableToUnlock(id)   // 해금 가능 — seedsCanUnlock + DefaultUnlock 폴백까지 처리
h.GetUnlockedSeedCount()
```

**`TotalMissions`는 전체 62개가 아니라 지금 해금된 것만.** 생성자가 `DefaultUnlock=true`를 자동 추가하고 이후 `AddFactionMission(id)`로 늘어난다. 오버레이가 보여줄 대상이 정확히 이것.

게임 내 종자 패널(`SeedUnLockUiState`)과 동일한 필터:
```csharp
TbSeedUnlock.DataList.Where(x => h.IsSeedNodeAvailableToUnlock(x.Id) || h.IsSeedNodeUnlocked(x.Id))
```

### (3) 보유 개수 & 표시 헬퍼
```csharp
DolocAPI.CountItem(itemName, checkBox: true)           // = InventorySystem.GetCount
DolocAPI.CanAfford(CountItem[] costs, checkBox: true)  // 전 항목 충족?
DolocAPI.CanAfford(costs, checkBox, scale)

DolocAPI.GetItemTitle(itemName)     // ⚠ 내부에서 GenerateItem() 할당 → 캐시 필수
DolocAPI.GetItemSprite(itemName)    // QueryItemProto 경유, 더 저렴
DolocAPI.GetNpcTitle(npcName, ignoreUnknown)
DolocUtils.GenerateCountItemInfo(CountItem[], separator)   // "이름 xN, 이름 xN" 리치텍스트
```
`checkBox: true`는 근처 상자까지 포함. 게임 패널이 `true`를 쓰므로 맞추는 게 좋다.

바로 쓸 수 있는 DTO (종자용):
```csharp
new SeedUnLockData(seedUnlockInfo)
    → icon, isUnLock, title, description, costInfo(서식 완료), enough(=CanAfford)
```

### (4) 갱신 트리거
```csharp
// 이벤트 허브 하나만 후킹하면 전부 잡힌다
[HarmonyPatch(typeof(DolocAPI), nameof(DolocAPI.Broadcast),
              new[] { typeof(GameEventType), typeof(GameEventArgs) })]
static void Postfix(GameEventType type) { /* 캐시 무효화 */ }
```
관심 이벤트: `SUBMIT_ITEM`, `UNLOCK_SEED`, `OBTAIN_ITEM`, `MAKE_ITEM`, `DROP_ITEM`, `USE_ITEM`, `SELL_ITEM_IN_STORE`

더 좁게 잡을 대안:
- `FactionMissionManager.SubmitFactionMissionItem(string, string)` Postfix
- `FactionMissionManager.AddFactionMission(string)` Postfix — 새 미션 해금
- `ArchiveOperationFarm.UnlockSeedNode` Postfix (static 확장 메서드도 패치 가능)

**폴링도 필요**: `LinearInventory`에 변경 이벤트나 버전 필드가 **없다.** 상자 입출고나 자동화 봇 경로는 이벤트로 안 잡힐 수 있으니 틱 폴링 + 스로틀(미니맵의 `FullRefreshInterval = 3.0` 패턴) 병행.

### (5) 필요 접점 요약 — 8개, 전부 public
| # | 접점 | 용도 |
|---|---|---|
| 1 | `DolocConfig.Tables.TbFactionMission` | 팩션 필요 아이템 |
| 2 | `DolocConfig.Tables.TbSeedUnlock` | 종자 필요 아이템 |
| 3 | `DolocConfig.Tables.TbFactionType` | 세력명 |
| 4 | `DolocConfig.Tables.TbFactionMissionType` | 분류명 |
| 5 | `archiveHandle.cityData.factionMissionManager` | 해금된 미션 + 제출 상태 |
| 6 | `archiveHandle.IsSeedNodeUnlocked / IsSeedNodeAvailableToUnlock` | 종자 해금 상태 |
| 7 | `DolocAPI.CountItem(name, true)` | 보유 개수 |
| 8 | `DolocAPI.Broadcast` (Harmony Postfix) | 갱신 트리거 |

**리플렉션 불필요.** 미니맵 모드가 `CityMapPanel`에 리플렉션을 쓴 건 private 필드 때문이었고, 우리 대상엔 그런 게 없다.

### (6) 미니맵 모드에서 그대로 가져올 것
| 파일 / 요소 | 비고 |
|---|---|
| `BodyController_Update_Patch.cs` | tick 펌프, 그대로 |
| `AgentControllerState_UseItem/UseTool_Patch.cs` | 설정창 입력 차단, 그대로 |
| `DraggableWindow.cs` | 창 드래그, 그대로 |
| `SettingsPanel`의 `CreateRowContainer/CreateLabel/CreateButton` | UI 팩토리, 그대로 |
| `Plugin.FindKoreanFont()` | **필수** — 텍스트 위주 UI |
| Canvas 생성 + CanvasScaler 복사 | 오버레이 캔버스 |
| `Config.Bind<Key>` 핫키 패턴 | 설정 |
| `DolocAPI.IsNormalState` 가드 | 메뉴/컷신 숨김 |

버릴 것: `BorrowPanel` / `ReleaseBorrowedPanel` / `DisableInternalMasks` / `RebindFogMasks` / `FindBestPanel` + `CityMapPanel` 리플렉션 4개.

### (7) 주의점
1. **줄 단위 완료는 기록되지만 줄 내부 부분 누적은 없음** — 게임이 전량 일괄 제출만 허용하므로 중간값이 존재하지 않음. 표시 가능한 상태는 완료 / 제출가능 / 부족 3가지
2. **종자 연구도 동일** — 한 번에 전액 지불. `12/50`은 모드 계산값이지 게임 상태가 아님
3. `GetItemTitle` / `GenerateItem`은 매 프레임 금지 (Item 할당) → `Dictionary<string,(string title, Sprite icon)>` 캐시
4. `TotalMissions`는 `.Values.ToArray()`로 매번 새 배열 → 매 프레임 금지, 캐시 후 이벤트 시 갱신
5. `plant_research_report`가 종자 18종 전부의 병목 → 별도 강조 고려
6. 상무부 정착(`SETTLED_TREATYPORT`) 전후로 접근 가능 미션이 달라지지만 `TotalMissions`가 이미 반영
7. 게임 패치 시 `Assembly-CSharp.dll`이 갱신되므로 타입·시그니처 재확인 필요 (본 문서 기준: 2026-08-18 빌드)

---

## 9. 미조사 / 남은 것

- [x] ~~DTMAPI 설치~~ — 2026-08-21 완료. `INSTALLED_HEALTHY`. 빌드 참조 8개 전부 확보 (§1, §2)
- [x] ~~DTMAPI 결합 수준 결정~~ — **(A) 순수 BepInEx 플러그인 확정.** 근거 §3
- [x] ~~`LogOutput.log` 기준선 확보~~ — 2026-08-21 완료. Error/Warning 0건, 패치 충돌 없음 확인 (§3)

**→ 조사 단계 종료. 이제 구현으로 넘어갈 수 있는 상태.**

남은 것:
- [ ] **UI 레이아웃 설계** — 표시 항목, 정렬 기준, 접기/펼치기, 핫키, 필터(완료 숨기기 등)
- [ ] **프로젝트 스캐폴딩** — csproj(net472 + HintPath 8개), `[BepInPlugin]` GUID/이름 확정,
      빌드 산출물을 `BepInEx\plugins\<모드명>\`에 배포하는 post-build 스텝
- [ ] `mission_tbmission.json` (159KB) 스토리 미션 노드 그래프 — 게시판 `COMPLETE_DIALOGUE` 36건의 실제 요구 아이템이 여기 있을 것
- [ ] `MissionRequireTemplate` 7종(`Order`/`Universal`/`ConditionTarget`/`MultiArgs`/`Accumulation` 등) — 실제 스토리 퀘스트가 쓰는 조합. 게임 실행 로그로 확인 필요
- [ ] 인게임 디버그 콘솔 — `RedSaw.CommandLineInterface` 네임스페이스 존재, `IMission`에 `[DebugObject]`/`[DebugInfo]` 어트리뷰트, LocalLow에 `redsaw_console_settings.json` 있음. 미션 데이터 덤프에 쓸 수 있을 가능성
- [ ] UI 레이아웃 설계 (표시 항목, 정렬, 접기/펼치기, 핫키)
