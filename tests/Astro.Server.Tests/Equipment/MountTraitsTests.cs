using Astro.Server.Equipment;
using Astro.Server.Equipment.Mount;

namespace Astro.Server.Tests.Equipment;

/// <summary>장비 › 적도의 (2026-10-17): N.I.N.A.의 적도의 이름·드라이버로 모델을 고르고, 모르는 적도의는 안전한 쪽</summary>
public class MountTraitsTests
{
    [Theory]
    [InlineData("On-Step", null, "umi17x")]                        // N.I.N.A.·PHD2가 알려 주는 이름
    [InlineData("Telescope", "ASCOM.OnStep.Telescope", "umi17x")]  // 이름이 흔해도 드라이버로
    [InlineData("Proxisky UMi17X", null, "umi17x")]
    [InlineData("EQMOD HEQ5/6", "ASCOM.EQMOD.Telescope", "generic")]
    [InlineData("", null, "generic")]
    [InlineData(null, null, "generic")]
    public void 이름과_드라이버로_모델을_고른다(string? name, string? driver, string expected) =>
        Assert.Equal(expected, MountCatalog.Match(name, driver).Id);

    [Fact]
    public void UMi17X_모델은_지금까지_코드에_있던_값과_같다()
    {
        var m = MountCatalog.UMi17X;
        Assert.Equal(60, m.PolarRotationDeg);         // 홈에서 한쪽으로 약 60°까지만 돈다
        Assert.Equal(2.0, m.RotationRateDegPerSec);
        Assert.True(m.HasHomeSensor);
    }

    [Fact]
    public void 모르는_적도의는_안전한_쪽()
    {
        var g = MountCatalog.Default;
        Assert.True(g.PolarRotationDeg < MountCatalog.UMi17X.PolarRotationDeg); // 회전을 작게
        Assert.False(g.HasHomeSensor);                                           // 홈 명령을 믿지 않는다
    }

    [Fact]
    public void 카탈로그는_공통_약속을_지킨다()
    {
        Assert.Equal("mount", Kind<MountCatalog, MountTraits>());
        Assert.Contains(MountCatalog.Default, MountCatalog.Models);
        Assert.Equal(MountCatalog.Models.Count, MountCatalog.Models.Select(m => m.Id).Distinct().Count());
    }

    private static string Kind<TCatalog, TModel>() where TCatalog : IEquipmentCatalog<TModel> where TModel : IEquipmentModel => TCatalog.Kind;
}
