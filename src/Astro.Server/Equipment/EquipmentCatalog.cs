namespace Astro.Server.Equipment;

// 장비 트리 (2026-10-17 사용자와 정한 용어 — docs/implementation/ARCHITECTURE.md 4.8):
//   장비 [분류 — 이 폴더, 자기 속성 없음]
//   └ 장비 종류 [카메라 · 적도의 · 포커서 … — 하위 폴더]
//       ├ 특성 [틀 — 그 종류라면 다 갖는 속성 목록, 예: CameraTraits]
//       ├ 모델 [틀에 값을 채운 묶음, 카탈로그의 한 줄 — 예: 후지 X 시리즈] · 기본 모델 [모르는 장비]
//       └ 카탈로그 [모델 목록 + 고르기]
//   배운 값 [써 보며 잰 값 — 모델 값보다 우선] · 지금 장비 [N.I.N.A.에 연결된(또는 프로필에 적힌) 장비] — EquipmentLearned
// 종류끼리 물려받을 속성은 없다(카메라의 바닥값과 적도의의 회전 한계는 무관) — 같은 것은 아래 공통 약속뿐.

/// <summary>모델 하나(특성 틀에 값을 채운 묶음)가 지키는 약속: 기록에 남길 이름</summary>
public interface IEquipmentModel
{
    /// <summary>바뀌지 않는 이름표 (배운 값·기록에)</summary>
    string Id { get; }
    /// <summary>사람이 읽을 이름</summary>
    string Name { get; }
}

/// <summary>
/// 장비 종류의 카탈로그가 지키는 공통 약속: 아는 모델 목록 · 기본 모델(모르는 장비) · 고르기(장비 이름·드라이버 → 모델).
/// 사용자는 보거나 고르지 않는다 — 아이라가 알아서 고른다
/// </summary>
public interface IEquipmentCatalog<TModel> where TModel : IEquipmentModel
{
    /// <summary>장비 종류 이름표 (배운 값·지금 장비를 종류별로 나눌 때). 예: "camera"</summary>
    static abstract string Kind { get; }
    static abstract IReadOnlyList<TModel> Models { get; }
    /// <summary>모르는 장비일 때 쓰는 모델</summary>
    static abstract TModel Default { get; }
    /// <summary>장비 이름(그리고 드라이버 설명)으로 모델을 고른다. 모르면 Default</summary>
    static abstract TModel Match(string? name, string? driver = null);
}
