using Astro.Server.Assistant;
using Astro.Server.Engine;
using Astro.Server.Sky;

namespace Astro.Server.Tests.Plan;

/// <summary>
/// 계획의 기본 노출·ISO 단계와 광학 계산 (Gemini 리뷰 GM-7-E-02): 망원경 배율·F값·픽셀 스케일은
/// 계획의 기본 노출과 가이드 오차 합격선(픽셀 스케일)에 바로 쓰인다
/// </summary>
public class PlanRulesAndOpticsTests
{
    [Theory]
    // 필터 · F값 · 가이드 · 밝은 달 → 노출(초) · ISO
    [InlineData("none", 4.0, true, false, 180, 800)]             // 밝은 광학계
    [InlineData("none", 5.5, true, false, 240, 800)]
    [InlineData("none", 7.0, true, false, 300, 800)]
    [InlineData("light_pollution", 5.0, true, false, 300, 800)]  // 240 × 1.3 = 312 → 30초 단위 내림
    [InlineData("dual_narrowband", 7.0, true, false, 600, 1600)] // 두 배, 600초까지
    [InlineData("dual_narrowband", 7.0, true, true, 600, 1600)]  // 좁은 대역은 달에 짧게 하지 않음
    [InlineData("none", 7.0, false, false, 120, 800)]            // 가이드 없으면 120초까지
    [InlineData("light_pollution", 4.0, true, true, 120, 800)]   // 234 → 210, 달 0.67배 → 120
    [InlineData("none", 4.0, false, true, 60, 800)]              // 120 × 0.67 → 60 (최소 60)
    [InlineData("none", 0, true, false, 240, 800)]               // F값을 모르면 5로 본다
    public void Default_Exposure_By_Filter_Focal_Ratio_Guider_And_Moon(string filter, double f, bool guiding, bool moon, int seconds, int iso)
    {
        var (exposure, gotIso, _) = PlanRules.DefaultExposure(filter, f, guiding, moon);
        Assert.Equal(seconds, exposure);
        Assert.Equal(iso, gotIso);
    }

    [Fact]
    public void Iso_Steps_For_Mirrorless_And_Gain_Range_For_Astro_Cameras()
    {
        Assert.False(PlanRules.IsStepIso(0, 400));    // 전용 천체 카메라(게인)
        Assert.True(PlanRules.IsStepIso(100, 12800)); // 미러리스
        Assert.True(PlanRules.IsStepIso(null, null)); // 연결 전·모의
        var x = PlanRules.IsoOptions(125, 1600);
        Assert.Equal(125, x[0]);
        Assert.Equal(1600, x[^1]);
        Assert.All(x, i => Assert.Contains(i, PlanRules.IsoStops));
        Assert.Equal(100, PlanRules.IsoOptions(null, null)[0]);
        Assert.Equal(12800, PlanRules.IsoOptions(null, null)[^1]);
        Assert.Equal([800], PlanRules.IsoOptions(30000, 40000)); // 단계가 하나도 없으면 800 하나
    }

    [Fact]
    public void Pixel_Scale_And_Field_Of_View()
    {
        // 3.76µm 화소, 6252×4176, 초점거리 500mm
        var rig = new Rig("scope", 500, 5, "cam", 3.76, 6252, 4176, false, true);
        Assert.Equal(206.265 * 3.76 / 500, rig.PixelScale, 6); // 약 1.55"/px
        Assert.Equal(2.692, rig.FieldOfView.Width, 2);          // 23.5mm 센서 가로
        Assert.Equal(1.798, rig.FieldOfView.Height, 2);
    }

    [Fact]
    public void Reducer_And_Flattener_Change_Focal_Length_And_Ratio()
    {
        var byAperture = new Scope("a", "망원경", 500, 100, null, 0.8, null);
        Assert.Equal(400, byAperture.EffectiveFocalLength, 6);
        Assert.Equal(4.0, byAperture.EffectiveFocalRatio, 6);
        Assert.Equal("400mm · f/4", byAperture.Optics);

        var byRatio = new Scope("b", "렌즈", 300, null, 4.5, null, 1.1);
        Assert.Equal(330, byRatio.EffectiveFocalLength, 6);
        Assert.Equal(4.95, byRatio.EffectiveFocalRatio, 6);

        // 배율을 모르거나 0 이하면 1로 본다, F값도 구경도 없으면 0
        var bare = new Scope("c", "?", 600, null, null, 0, -1);
        Assert.Equal(600, bare.EffectiveFocalLength, 6);
        Assert.Equal(0, bare.EffectiveFocalRatio, 6);
    }
}
