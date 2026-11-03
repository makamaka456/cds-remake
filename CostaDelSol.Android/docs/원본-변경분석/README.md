# 원본 변경 분석 (cds-helper f4c2d62 ≈ cds-remake v1.0.0  →  cds-remake v1.1.65)

2026-11-03 에 만든 사전 분석이다. 새 방(cds-remake 포크)에서 처음부터 다시 셈하지 말고 여기부터 본다.

| 파일 | 내용 |
|---|---|
| `링크한-파일-변화.md` | 우리가 링크한 PC 파일 324개가 v1.1.65 에서 어떻게 됐나 — 그대로 41 · **수정됨 280** · 이름 바뀜 0 · **없어짐 3**(ShipRepairDialog · ShipSellDialog · SphinxCalculatorContentViewModel) |
| `대용품-API-변화.md` | 우리가 같은 이름으로 다시 만든 PC 클래스(ChoiceDialog 등)의 공개 멤버 변화 — **대용품도 맞춰야 컴파일된다** |
| `pc-edits-from-f4c2d62.patch` | 안드로이드 폴더 **밖**에서 우리가 고친 PC 파일 14개(269줄)의 패치. `git apply --3way` 시험 결과는 인계-노트 §5 |

## 원본의 큰 변화 (알아 둘 것)
- 저장소가 `Kyeongrok/cds-helper` → **`Kyeongrok/cds-remake`** 로 옮겨졌다(첫 커밋 2026-09-21). 커밋 1,166개, v1.1.0~v1.1.65, 릴리즈 노트 `릴리즈노트.md`.
- **이름 바뀜**: `ShipMapWindow` → `WorldMapWindow`(6,305 → 8,321줄), `ShipMapHost` → `WorldMapHost`(2,533 → 3,811줄), `HintListDialog` → `ListPickDialog`. 우리 갈래 파일 이름은 그대로 두어도 되지만 PC 쪽 이름 따라가기는 필요하다.
- **새 부품**: `UserDataFolder`(자료 폴더를 한 곳에서 정함, 환경변수 `CDS_SAVEDIR` — 옛 `GameSave.FolderOverride` 대체), `OffscreenTest`/`OffscreenHarness`(원본 제작자의 화면 밖 시험 틀), 애드온(`UI/AddOns`, Lua 저장 `LuaSaved`), 다국어(`Lang`, `ILanguage`, `TranslatedLanguage`), 버그 리포트(`BugReport`, `CrashReport`), 사용자 발견물·시나리오(`UserDiscoveries`, `Scenario`), 작위·귀양·하드코어(`Nobility`, `Exile`, `HardcoreRules`), 도시 성장(`CityGrowth`), 동영상 받기(`MovieAssetDownloader`, 릴리즈 `movie-assets`), 확대 모형·지도 셰이더(`MapShaderData`) 등. 사용자와 **무엇을 안드로이드에 넣을지** 정해야 한다.
- 새 저장소에도 우리 버그 두 개의 **원본 수정이 이미 들어 있다**(인계-노트 §6).
