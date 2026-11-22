# 지하 연구소 맵 (튜토리얼 + 중반 전투) — 작업 문서

보스방 **입구 직전까지**의 탑다운 연구소 맵입니다. 보스맵은 만들지 않았습니다.

| 항목 | 경로 |
|---|---|
| 씬 | `Assets/_Game/Maps/Laboratory/Scenes/Laboratory_Prototype.unity` |
| 맵 프리팹 | `Assets/_Game/Maps/Laboratory/Prefabs/LaboratoryMap.prefab` |
| 적 프리팹 Variant | `Assets/_Game/Maps/Laboratory/Prefabs/Enemies/LabMob.prefab` (TestMob 의 Variant) |
| 런타임 스크립트 | `Assets/_Game/Maps/Laboratory/Scripts/` (네임스페이스 `Unleash.Laboratory`) |
| 생성기 / 점검 도구 | `Assets/_Game/Maps/Laboratory/Editor/LaboratoryMapBuilder.cs`, `LaboratoryMapValidator.cs` |
| 자동 생성 리소스 | `Assets/_Game/Maps/Laboratory/Generated/` (타일 아틀라스, Tile, 문 애니메이션) |

## 재생성 / 점검

- **UNLEASH ▸ Laboratory ▸ Build Map + Scene** : 맵 프리팹과 씬을 코드로 다시 만듭니다.
  - ⚠️ 다시 빌드하면 `LaboratoryMap.prefab` 과 씬이 **덮어써집니다**. 손으로 고친 내용을 유지하려면
    빌더(`LaboratoryMapBuilder.cs`)에 반영하거나, 손으로 고치기 시작한 이후에는 빌더를 다시 돌리지 마세요.
- **UNLEASH ▸ Laboratory ▸ Validate** : Missing Script/Reference 검사, 시작점→보스방 입구 이동 가능 여부(실제 Wall 콜라이더 기준 BFS), 적 배치 수를 콘솔에 출력합니다.

## 진행 흐름 (남 → 북)

```
Z0 로비(시작) → Z1 어두운 복도 → Z2 첫 습격 → Z3 도주 복도(동쪽) → Z4 조명실
 → Z5 손전등 구간 → Z6 연구동 복도(방 매복 R1~R3) → Z7 대형 홀(조명 ON)
 → Z8 좁은 골목(양방향 협공) → Z9 디펜스(보스 셔터) → Z10 보스방 전실(종료)
```

1 타일 = 1 유닛(16px, 16 PPU). 메인 통로는 5칸 폭, 문은 2칸 폭, 좁은 골목만 3칸 폭입니다.
장애물은 벽에 붙여 배치했고 통로 한가운데에는 두지 않았습니다 (대형 홀의 엄폐물 4개만 예외, 그 사이 통로는 7칸 이상).

| 구역 | 연출 의도 | 적 | 트리거 |
|---|---|---|---|
| Z0 로비 | 조작 힌트(WASD/대시/F), 깜빡이는 조명 | 없음 | 문 접근 시 자동 열림 |
| Z1 복도 | 붉은 비상등, 점액·핏자국, 떨어진 장비, "BRAVO TEAM… SIGNAL LOST" | 없음 | — |
| Z2 첫 습격 | 동료가 당한 현장(피·점액·넘어진 바리케이드), 진입 시 경보등 점등 | 근접 3 (`Mobs_Z2_Assault`) | `Encounter_Z2_Assault` : 감지반경 14로 습격, "BULLETS DON'T STOP THEM / RUN" |
| Z3 도주 복도 | 화살표 데칼로 동쪽 안내 | (Z2 적이 추격) | — |
| Z4 조명실 | 북쪽에 살아 있는 조명 1개 (강한 명암 대비) | 근접 2 (`Mobs_Z4_LightDemo`) | `Encounter_Z4_LightDemo` : 진입 시 적 2마리 추격 |
| Z5 손전등 구간 | 어두운 사무실, 출구 앞을 적이 막음, "F FLASHLIGHT" 안내 | 근접 3 (`Mobs_Z5_Flashlight`) | `Encounter_Z5_Flashlight` : 진입 시 적 활성화 |
| Z6 연구동 복도 | 방 3곳 문이 열리며 적이 튀어나옴 | R1 2 / R2 2 / R3 3, 복도 배회 2 | 각 문 앞 `Encounter_R*_Ambush` (문 열림 + 감지반경 16) |
| Z7 대형 홀 | 어둠 속 적 무리, 입구 콘솔 **E → 메인 조명 ON** | 근접 10 (`Mobs_Z7_Hall`, 3무리) | `LightSwitch_MainPower` : 메인 조명이 깜빡이며 켜지고 비상등 꺼짐 (적은 평소대로 근처에 오면 반응) |
| Z8 좁은 골목 | 3칸 폭, 붉은 점멸등 | 서쪽 4 + 동쪽 3 | 합류 지점 진입 시 양 끝에서 동시에 몰려옴, **10초 후** 디펜스실 문 해제 |
| Z9 디펜스 | 콘솔 **E** → 보스 셔터 2장이 45초에 걸쳐 열림, 입구 문 잠김, 경보 | 웨이브 7회(3~4마리), 동시 최대 10 | `DefenseConsole` (`LabDefenseEvent`), WavePoint 8곳(서·동·북·남 각 2) |
| Z10 보스방 전실 | 보라색 불길한 조명, 잠긴 보스방 문 | 없음 | `BossRoomEntrance_End` : "BOSS AREA AHEAD - PROTOTYPE END" |

배치 적 합계 34 + 디펜스 웨이브(총 25마리 예정, 동시 최대 10).
진행은 **처치 수가 아닌 시간/위치 조건**으로만 막습니다 → 현재처럼 쉴드 때문에 총으로 잘 안 죽어도 소프트락이 없습니다.

마커: `Markers/PlayerStart`, `Markers/Z8_SpawnPoints/SpawnPoint_*`, `Markers/Z9_WavePoints/WavePoint_*`, `Markers/BossRoomEntrance_End`.

## 적 (기존 시스템 연결)

- 모든 적은 `Prefabs/TestMob/MobController.prefab` 인스턴스(`Mobs_*`)의 **자식**으로 들어가 있습니다.
  `Monster.Start()` 의 `GetComponentInParent<MobController>()` 등록이 그대로 동작합니다.
- 적 프리팹은 `TestMob.prefab` 의 **Variant `LabMob`** 입니다. 추가된 것은 자식 `WallBlocker`(CircleCollider2D r=0.32, 트리거 아님) 하나뿐입니다.
  원본 TestMob 은 트리거 콜라이더만 있어서 벽을 통과했기 때문에, 벽/닫힌 문에 막히고 적끼리 겹치지 않게 하려고 추가했습니다.
  피격(Hitbox, 태그 Monster)·감지·공격 로직은 원본 그대로입니다.
- 매복/웨이브 적은 `LabMobUtil.SetAggroRadius()` 로 `DetectRange` 트리거의 스케일만 키워 멀리서도 쫓아오게 합니다 (스크립트 수정 없음).
- 디펜스 웨이브는 `LabMob` 을 `Mobs_Z9_Waves`(MobController) 자식으로 Instantiate 합니다.

## 문 애니메이션

- 에셋의 `animations/objects/doors/*_open.png`, `*_close.png` (각 12프레임, 32×32) 를 슬라이스해서 사용합니다.
- **AnimatorController 1개** (`Generated/Animations/Doors/LabDoor_Base.controller`)
  - 상태: `Closed` → `Opening` → `Open` → `Closing` → `Closed`
  - 파라미터: `IsOpen`(bool), `AnimSpeed`(float, Opening/Closing 재생 속도)
- 문 종류별 **AnimatorOverrideController** : `slide_lobby`, `door_lab`, `door_contain`, `door_reactor`, `shutter_reactor`(보스 셔터), `shutter_contain`(보스방 문)
  → 새 문 종류는 빌더의 `DoorKinds` 배열에 이름만 추가하면 클립/오버라이드가 생성됩니다.
- `LabDoor` 컴포넌트: `Open()`, `ForceOpen(duration)`, `Close()`, `Lock()`, `Unlock()`, `State`, `Progress`, `StateChanged` 이벤트.
  `openDuration` 으로 열리는 속도를 바꾸면 애니메이션 속도가 자동으로 맞춰집니다 (보스 셔터 45초).
  - 닫혀 있는 동안(열림 진행도 75% 전까지) Wall 레이어 BoxCollider2D 로 플레이어·적·총알을 막습니다.
  - 자식 `Sensor`(트리거)가 플레이어를 감지해 자동으로 엽니다 (`autoOpenOnPlayer`, 잠긴 문은 무시).
  - 문 안에 플레이어가 서 있으면 닫기를 미룹니다.
- 문 배치: 로비 출구(slide_lobby), 연구실 문 R1·R2(door_lab), 격리실 R3·홀 입구(door_contain), 골목·디펜스실(door_reactor), 보스 셔터 2장(shutter_reactor), 보스방 문(shutter_contain, 잠김).
- 에셋의 문 그림이 정면(북쪽 벽면)용이라 문은 모두 남북 방향 통로에 있습니다.

## 충돌 / 총알-벽

- **Layer 8 `Wall`** 을 추가했습니다 (ProjectSettings/TagManager 의 빈 슬롯 1칸, 충돌 매트릭스는 기본값 그대로).
- 벽: `Grid/WallCollision` 타일맵 = TilemapCollider2D + CompositeCollider2D(Polygons) + Static Rigidbody2D, Layer `Wall`.
  바닥에서 2칸 이내의 모든 벽 칸에 충돌 타일이 깔려 있습니다 (보이는 벽 그림과 별개).
- 닫힌 문, 큰 소품(책상·크레이트·서버랙 등 `collide` 소품)도 Layer `Wall`.
- `Bullet.cs` (기존 파일, **최소 수정**): `OnTriggerEnter2D` 에 `else if` 한 줄 추가 —
  부딪힌 콜라이더가 **트리거가 아니고 Layer 가 `Wall`** 이면 총알을 파괴합니다. `Monster` 태그 처리는 그대로이며,
  `Wall` 레이어 오브젝트가 없는 기존 Combat_Test 에서는 동작이 바뀌지 않습니다.
  - 장애물을 새로 만들 때 총알을 막고 싶으면 **Layer 를 `Wall` 로, 콜라이더는 트리거 해제** 하면 됩니다.

## 조명 (환경 연출 전용)

- 씬 GlobalLight 강도 0.2(푸른 톤) → 기본적으로 어둡고, 구역별 Light2D 로 분위기를 냅니다.
  `LabFlickerLight` (고장 깜빡임 / 경보 점멸 / 숨쉬기)로 비상등·경고등·실험실 조명을 표현합니다.
- `LabLightZone` : 조명 그룹(Light2D 목록 + 켜기/끄기 + 에디터 표시용 영역). 현재 Z7 홀 메인 조명 1개.
- `LabLightSwitch` : E 키로 조명 그룹을 켜고 비상등을 끄는 스위치.
- **맵의 조명은 적에게 아무 영향도 주지 않습니다** (피해·밀어내기·색 변경·감지범위 변경 없음).
  빛과 적의 상호작용은 기존 손전등/적 시스템(팀 작업)에만 맡깁니다.
- 등명기(`SearchLightController`)는 E 키를 같이 쓰므로 이 맵에는 배치하지 않았습니다.

## 직접 확인할 테스트 포인트

1. 로비에서 시작 → 문 앞에서 유리문 열림 애니메이션.
2. Z2 진입 → 경보등 + 적 3마리 습격, 총을 쏴도 쉴드 때문에 피해 없음 → 동쪽으로 도주.
3. Z4 진입 시 적 2마리가 쫓아오는지 (조명은 분위기 연출만, 적에게 영향 없음).
4. Z5 진입 시 적 3마리가 활성화되는지.
5. Z6 각 방 문 앞을 지나가면 문이 열리고 적이 튀어나오는지 (R1·R3 위쪽, R2 아래쪽).
6. Z7 입구 콘솔에서 E → 메인 조명이 깜빡이며 켜지고 비상등이 꺼지는지 (적 색·능력치 변화 없음).
7. Z8 합류 지점에서 양쪽 끝 적이 몰려오고, 약 10초 후 디펜스실 문이 열리는지.
8. Z9 콘솔 E → 입구 문 닫힘, 셔터가 45초 동안 천천히 열리며 사방에서 웨이브, 끝나면 "GATE OPEN".
9. Z10 도착 시 "PROTOTYPE END" 문구.
10. 벽·닫힌 문·큰 소품에 총을 쏘면 총알이 사라지는지 / 적에게는 기존처럼 맞는지("Hit!").

## 알려진 제한

- 기존 적 AI 는 길찾기 없이 플레이어 방향으로 직선 이동합니다. 엄폐물·문틀 모서리 뒤에서는 걸릴 수 있어서,
  매복 적은 문 열(2칸) 안쪽에 세로로 배치해 두었습니다.
- 안내 문구는 TMP 기본 폰트(LiberationSans)에 한글 글리프가 없어 영어로 작성했습니다. 한글 폰트 에셋을 추가하면 바꿀 수 있습니다.
- 씬을 Build Settings 에는 추가하지 않았습니다 (ProjectSettings 변경 최소화). 빌드에 넣으려면 File ▸ Build Settings 에서 추가하세요.

## 파일 수준 변경 내역

- **수정된 기존 파일**
  - `Assets/_Game/Scripts/Combat/Bullet.cs` — Wall 레이어 충돌 시 총알 파괴 (13줄 추가, 기존 로직 유지)
  - `ProjectSettings/TagManager.asset` — Layer 8 `Wall` 추가
  - 연구소 타일셋 에셋의 일부 `.png.meta` (59개) — 임포트 설정만 변경: 16 PPU, Point 필터, 무압축, 문/애니메이션 시트 슬라이스.
    PNG 원본은 그대로이며, 이 에셋을 쓰던 기존 씬/프리팹은 없었습니다.
- **수정하지 않은 것**: `Combat_Test.unity`, Player/Bullet/TestMob/MobController/Flashlight/등명기 프리팹, Monster·Flashlight·PlayerMover 등 모든 기존 스크립트, 카메라 설정.
- **새 파일**: `Assets/_Game/Maps/Laboratory/` 이하 전부.
