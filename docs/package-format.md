# 텍스트 패키지 스키마 1

런처의 첫 적용 시 설치된 게임에서 패키지를 생성합니다. 원본 PAK, 캠페인, 글꼴을 저장소에서 내려받지 않습니다.

```text
ko/
  manifest.json
  files/
    Packs/data.pak
    adventures/Darkling Campaign.bfhp
    adventures/Forester Campaign.bfhp
    adventures/Human Campaign.bfhp
    adventures/Tutorial.BFHP
    Local/Fonts/*.apf
    Local/English/game.loc.xml
```

매니페스트 주요 필드:

| 필드 | 의미 |
| --- | --- |
| `Schema` | 1 |
| `Version` | 현재 `0.3.3-resources` |
| `Scope` | `game-text` |
| `TranslationMethod` | 기계번역 초안 및 수동 용어 보정 |
| `TotalStrings`, `TranslatedStrings` | 패키지에 실제 포함한 고유 키 수. 두 값이 같아야 함 |
| `FontFormatVerified` | APF 문자 범위 및 RLE 검증 결과 |
| `RuntimeVerified` | 실제 게임 화면 검수 여부. 현재 `false` |
| `Complete`, `KoreanFontVerified` | 검수 완료 선언. 현재 둘 다 `false` |
| `Files` | 각 파일의 상대 경로, `OriginalSha256`, `PatchedSha256` |

텍스트 초안은 `Scope=game-text`이고 `FontFormatVerified=true`인 경우 설치할 수 있습니다. 이 조건은 품질 검수나 게임 실행 성공을 자동으로 보장하지 않습니다. 이전 일반 패키지 형식은 `Complete`와 `KoreanFontVerified`를 모두 요구합니다.

대상 파일은 이미 존재해야 하며 원본 해시가 일치해야 합니다. `Packs/`, `Local/`, `adventures/`, `Scenarios/` 아래만 허용하고 EXE·DLL·스크립트는 설치하지 않습니다. 경로 이탈, 링크 경로, 중복 대상과 손상된 파일을 거부합니다. 해시 검증은 서명 검증이 아니므로 신뢰하는 제작자의 패키지만 사용하세요.

v0.4.7부터 `Created=true`인 신규 파일은 `Local/English/game.loc.xml` 하나만 허용합니다. 이 경우 `OriginalSha256`는 0 64개로 '원본에 없음'을 나타내며 기존 파일·디렉터리가 있으면 설치를 거부합니다. 원본 백업 대신 부재 상태를 복구 기록에 보관하고, 복원 시 설치 해시가 일치하는 신규 파일만 제거합니다. 다른 파일의 원본 해시·백업 검증은 그대로 적용합니다.

생성 파일은 게임 원본 및 로컬 글꼴에서 파생되므로 공개 저장소에 업로드하지 않습니다. 배포 시 해당 리소스의 배포 권한을 별도로 확인해야 합니다.
