# 패치 패키지 형식 (스키마 1)

이 문서는 향후 완성된 번역 리소스의 설치 형식입니다. 이 저장소에는 완성된 번역 패키지가 없습니다.

```text
ko/
  manifest.json
  files/
    Packs/data.pak
    Local/Fonts/Tahoma13.apf
    ...
```

```json
{
  "Schema": 1,
  "Version": "1.0.0",
  "Complete": true,
  "KoreanFontVerified": true,
  "TranslatedStrings": 1,
  "TotalStrings": 1,
  "Files": [
    {
      "Path": "Packs/data.pak",
      "OriginalSha256": "원본 파일의 소문자 SHA-256 64자리로 교체",
      "PatchedSha256": "패치 파일의 소문자 SHA-256 64자리로 교체"
    }
  ]
}
```

예제 숫자 1은 형식 설명용이며 실제 번역 수가 아닙니다. 범위를 조사한 실제 수치로 바꾸어야 합니다. `Complete`와 `KoreanFontVerified`는 제작자의 검수 선언으로, 런처가 번역 품질이나 게임 렌더링을 자동 판정한다는 뜻은 아닙니다. 신뢰하는 제작자의 패키지만 사용하세요.

대상 파일은 이미 존재해야 하며 원본 해시가 일치해야 합니다. 대상 경로는 `Packs/`, `Local/`, `adventures/`, `Scenarios/` 아래만 허용합니다. 실행 파일과 스크립트는 설치하지 않습니다. 경로 이탈, 링크 경로, 중복 대상 및 손상 파일은 거부합니다.

패키지에 게임 전체 원본을 포함한 파일을 배포할 때는 배포 권한을 별도로 확인해야 합니다. 현재 저장소는 게임 원본을 포함하지 않습니다.
