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

## 진행 흐름 (확장판)

```
Z0 로비 → Z1 비상등 복도 → Z2 첫 습격 → Z3 도주 → Z4 조명실(빛 약점) → Z5 손전등 사무실
 → Z6 연구동 복도 (R1 매복 / R2 직원 휴게실[휴식, 선택] / R3 봉인 표본실[보이기만 함])
 → Z7 대형 홀 (어둠 속 10마리, 콘솔 E → 메인 조명 ON)
 → 홀 서쪽 열린 통로 → [Sector C]
      C0 장거리 연구 복도 ─┬─ C1 화학실(선택: 어둠 속 4 + 맨 안쪽 전력 패널) → C2 저장실(보급, 패널로 해제)
                           ├─ C8 생물실(선택: 탐색 + 배회 2, 변이체의 정체 암시)
                           └─ C3 관찰 복도 (유리 너머 격리 칸 표본 5마리가 창에 들러붙음)
                                → C4 격리실 봉쇄 (입구 잠김 → 그 표본들이 칸에서 풀려남 → 20초 후 해제)
 → C5 휴게실[휴식] → C6 비상 계단(붉은 경고등) → Z8 좁은 골목 (뒤 문 잠김 + 앞뒤 협공, 10초)
 → C7 보안실[휴식] → Z9 디펜스(45초) → Z10 보스방 전실(종료)
```

전투 강도: 탐색 → 작은 전투(Z2) → 도주 → 작은 전투(Z4·Z5) → 매복(R1) → 휴식(R2) → **큰 전투(Z7)**
→ 탐색/선택(C0·C1·C8) → 긴장(C3 관찰) → **봉쇄(C4)** → 휴식(C5) → 분위기(C6) → **협공(Z8)** → 휴식(C7) → **디펜스(Z9)**.
강한 전투 사이에는 항상 휴식/탐색 구간이 하나 이상 있습니다.

1 타일 = 1 유닛(16px, 16 PPU). 메인 통로는 5칸 폭, 문은 2칸 폭, 좁은 골목만 3칸 폭입니다.
장식 소품은 벽/가장자리에 붙였고, 통로 한가운데 장애물은 의도된 엄폐물(대형 홀 4개, C0 벽 돌출부 8개, C4 2개)만 있습니다.
바닥 면적 2,823칸 → 4,230칸 (약 1.5배).

| 구역 | 연출 의도 | 적 | 트리거 |
|---|---|---|---|
| Z0 로비 | 조작 힌트(WASD/대시/F), 깜빡이는 조명 | 없음 | 문 접근 시 자동 열림 |
| Z1 복도 | 붉은 비상등, 점액·핏자국, "BRAVO TEAM… SIGNAL LOST" | 없음 | — |
| Z2 첫 습격 | 동료가 당한 현장, 진입 시 경보등 | 3 (`Mobs_Z2_Assault`) | `Encounter_Z2_Assault` (감지 14) |
| Z3 도주 복도 | 화살표 데칼로 동쪽 안내 | (Z2 적 추격) | — |
| Z4 조명실 | 살아 있는 조명 1개 | 2 | `Encounter_Z4_LightDemo` |
| Z5 손전등 사무실 | 어두운 사무실, "F FLASHLIGHT" | 3 | `Encounter_Z5_Flashlight` |
| Z6 연구동 복도 | R1 만 문이 열리며 매복. 복도 배회 2 | R1 2, 배회 2 | `Encounter_R1_Ambush` |
| R2 직원 휴게실 | 따뜻한 조명, 자판기/정수기. **적 없음 (홀 직전 휴식)** | 0 | — |
| R3 봉인 표본실 | 문 영구 잠김. 복도를 지나가면 안의 표본이 깨어나 벽에 들러붙는다 | 2 (`Mobs_R3_Vault`, 못 나옴) | `Encounter_R3_VaultStir` (감지 8) |
| Z7 대형 홀 | 어둠 속 적 무리, 입구 콘솔 **E → 메인 조명 ON**. 출구는 서쪽 열린 통로 | 10 | `LightSwitch_MainPower` |
| C0 장거리 연구 복도 | 50칸 직선 시야, 남/북 벽 돌출 엄폐물 8개(원거리 적 대비), 갈림길 표지 | 배회 2 | `Encounter_C0_Roam` (홀에서 넘어올 때 활성, 감지 6) |
| C1 화학실 (선택) | 어두운 실험실. 적을 지나 **맨 안쪽 전력 패널 E** → 조명 ON + 옆 저장실 잠금 해제 | 4 | `Encounter_C1_Chemistry`, `PowerPanel_Chemistry` (`LabPowerConsole`) |
| C2 저장실 (선택) | 잠긴 보급실. 탄약/배터리/연구자료 Spawn 마커 | 0 | 화학실 패널로 해제 |
| C8 생물실 (선택) | 표본 수조, "SUBJECT M-2 : ORIGIN - LAB STAFF" | 2 | `Encounter_C8_Biology` |
| C3 관찰 복도 | 관찰창 너머 격리 칸 3곳. 표본 5마리가 깨어나 창으로 몰려온다 (공격 불가) | 5 (`Mobs_C3_Specimens`) | `Encounter_C3_SpecimensWake` (감지 16) |
| C4 격리실 봉쇄 | 방에 2칸 들어서면 **입구 잠김 + 격리 칸 문 3개 개방 + 경보**. 20초 후 출구·입구 해제 | (C3 의 5마리) | `Encounter_C4_Lockdown` |
| C5 휴게실 | 봉쇄 직후 휴식. 적 없음 | 0 | — |
| C6 비상 계단 | 붉은 경고등만 켜진 좁은 통로 (분위기 전환) | 0 | — |
| Z8 좁은 골목 | 서쪽 문으로 들어와 중앙 거점(10×4)에 서면 **뒤 문 잠김**, 지나온 서쪽 + 앞쪽 동쪽에서 동시에 몰려옴. 10초 후 보안실 문 해제 | 서 4 + 동 4 | `Encounter_Z8_Alley` |
| C7 보안실 | 디펜스 직전 휴식. 모니터 조명 | 0 | — |
| Z9 디펜스 | 콘솔 **E** → 보스 셔터 45초, 보안실 쪽 문 잠김, 경보 | 웨이브 7회, 동시 최대 10 | `DefenseConsole` (`LabDefenseEvent`) |
| Z10 보스방 전실 | 보라색 조명, 잠긴 보스방 문 | 없음 | `BossRoomEntrance_End` |

배치 적 합계 45 + 디펜스 웨이브. 게임 시작 시 활성 상태인 적은 Z6 배회 2 + Z7 홀 10 뿐이고,
나머지는 해당 구역에 다가갈 때 `LabEncounter` 로 켜집니다 (동시에 너무 많이 켜지지 않게).
진행은 **처치 수가 아닌 시간/위치 조건**으로만 막습니다 → 쉴드 때문에 총으로 잘 안 죽어도 소프트락이 없습니다.

마커: `Markers/PlayerStart`, `Markers/Z8_SpawnPoints/*`, `Markers/Z9_WavePoints/*`, `Markers/BossRoomEntrance_End`,
`Markers/RangedFuture/Spawn_Ranged_Future_*` (원거리 적 예정 위치 6곳, 지금은 빈 마커),
`Markers/C2_Storage_Supplies/*`, `C5_BreakRoom_Supplies`, `C7_Security_Supplies`, `C8_Biology_Supplies` (탄약/배터리/연구자료 예정 위치).

## 적 (기존 시스템 연결)

- 모든 적은 `Prefabs/TestMob/MobController.prefab` 인스턴스(`Mobs_*`)의 **자식**으로 들어가 있습니다.
  `Monster.Start()` 의 `GetComponentInParent<MobController>()` 등록이 그대로 동작합니다.
- **모든 근거리 적(배치·매복·디펜스 웨이브)은 현재 `LabMob.prefab` 을 사용합니다.** 빌더는 LabMob 이 이미 있으면 절대 다시 만들지 않습니다
  (M2 외형 `Sprite/M2Visual` + `LabMobDirectionalSprite` 유지). Validate 메뉴가 LabMob 이 아닌 적을 검사합니다.
- 적 프리팹은 `TestMob.prefab` 의 **Variant `LabMob`** 입니다. 추가된 것은 자식 `WallBlocker`(CircleCollider2D r=0.32, 트리거 아님)와 M2 외형(`Sprite/M2Visual`)입니다.
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
- `LabPowerConsole` : E 키로 조명 그룹 ON + 잠긴 문 해제(+열기) + 오브젝트 켜기/끄기. (화학실 패널 → 저장실 해제)
- **맵의 조명은 적에게 아무 영향도 주지 않습니다** (피해·밀어내기·색 변경·감지범위 변경 없음).
  빛과 적의 상호작용은 기존 손전등/적 시스템(팀 작업)에만 맡깁니다.
- 등명기(`SearchLightController`)는 E 키를 같이 쓰므로 이 맵에는 배치하지 않았습니다.

## 직접 확인할 테스트 포인트

1. 로비에서 시작 → 문 앞에서 유리문 열림 애니메이션.
2. Z2 진입 → 경보등 + 적 3마리 습격, 총을 쏴도 쉴드 때문에 피해 없음 → 동쪽으로 도주.
3. Z4 진입 시 적 2마리가 쫓아오는지 (조명은 분위기 연출만, 적에게 영향 없음).
4. Z5 진입 시 적 3마리가 활성화되는지.
5. Z6: R1 문 앞에서만 매복, R2 는 적 없는 휴게실, R3 앞을 지나가면 봉인된 방 안 표본이 벽에 들러붙는지.
6. Z7 입구 콘솔에서 E → 메인 조명 ON. 홀 서쪽 통로("<<< SECTOR C")로 Sector C 진입.
7. C1 화학실 맨 안쪽 패널 E → 화학실 조명 ON + 저장실 문 열림. C3 관찰 복도에서 창 너머 표본 5마리가 몰려오는지.
8. C4 격리실에 들어서면 입구 잠김 + 격리 칸 3개 열림 + 경보, 20초 후 출구 열림 → C5 휴게실 → C6 계단 → 골목.
9. Z8 중앙 거점에서 뒤(서쪽) 문이 잠기고 양쪽에서 몰려오며, 약 10초 후 보안실 문이 열리는지.
10. Z9 콘솔 E → 보안실 쪽 문 닫힘, 셔터가 45초 동안 열리며 사방에서 웨이브, 끝나면 "GATE OPEN".
11. Z10 도착 시 "PROTOTYPE END" 문구.
12. 벽·닫힌 문·큰 소품에 총을 쏘면 총알이 사라지는지 / 적에게는 기존처럼 맞는지("Hit!").

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
