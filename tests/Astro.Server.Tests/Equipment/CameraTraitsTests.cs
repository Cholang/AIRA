using Astro.Server.Equipment;
using Astro.Server.Equipment.Camera;
using Astro.Server.Shoot;

namespace Astro.Server.Tests.Equipment;

/// <summary>장비 › 카메라 (2026-10-17): 이름으로 모델을 고르고, 카메라마다 잰 장 사이 쉬는 시간을 배운 값으로 따로 기억한다</summary>
public class CameraTraitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aira-eq-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    [Theory]
    [InlineData("X-T5", null, "fuji-x")]
    [InlineData("Fujifilm X-T5", null, "fuji-x")]                 // 사진 머리글 INSTRUME 모양
    [InlineData("X-T5 (gin_X-T5)", null, "fuji-x")]               // N.I.N.A. 장치 이름
    [InlineData("GFX100S", null, "fuji-gfx")]
    [InlineData("Camera", "Fujifilm plugin GFX 50S", "fuji-gfx")]  // 이름이 흔해도 드라이버로
    [InlineData("Camera", "Fujifilm plugin", "fuji-x")]
    [InlineData("ZWO ASI2600MC Pro", "ZWO ASCOM", "generic")]
    [InlineData("", null, "generic")]
    [InlineData(null, null, "generic")]
    public void 이름과_드라이버로_모델을_고른다(string? name, string? driver, string expected) =>
        Assert.Equal(expected, CameraCatalog.Match(name, driver).Id);

    [Fact]
    public void 카탈로그는_공통_약속을_지킨다()
    {
        Assert.Equal("camera", Kind<CameraCatalog, CameraTraits>());
        Assert.Same(CameraCatalog.Generic, CameraCatalog.Default);
        Assert.Contains(CameraCatalog.FujiX, CameraCatalog.Models);
        Assert.Equal(CameraCatalog.Models.Count, CameraCatalog.Models.Select(m => m.Id).Distinct().Count());
    }

    private static string Kind<TCatalog, TModel>() where TCatalog : IEquipmentCatalog<TModel> where TModel : IEquipmentModel => TCatalog.Kind;

    [Fact]
    public void X_T5_모델은_지금까지_코드에_흩어져_있던_값과_같다()
    {
        var x = CameraCatalog.FujiX;
        Assert.Equal((16383, 1024), (x.RawWhite, x.BlackLevel));
        Assert.False(x.CanAbortExposure);
        Assert.True(x.OverRangeIsSaturation);
        Assert.Equal(6, x.ColorBlock);       // X-Trans 6×6
        Assert.Equal(1, x.ShapeSmoothing);   // 3×3
        var g = CameraCatalog.Generic;
        Assert.Null(g.RawWhite);
        Assert.True(g.CanAbortExposure);
        Assert.Equal(6, g.ColorBlock);       // 모르면 어느 컬러 센서에도 맞는 6×6
    }

    [Fact]
    public void 배운_값이_없으면_모델_값_있으면_최근_밤들의_중앙값()
    {
        var learned = new EquipmentLearned(_dir);
        learned.SetCurrentCamera("X-T5");
        Assert.Equal(CameraCatalog.FujiX.FrameOverheadSeconds, learned.FrameOverheadSeconds());
        foreach (var s in new[] { 30.0, 8, 9, 12 }) learned.RecordFrameOverhead(s, DateTimeOffset.Now);
        Assert.Equal(10.5, learned.FrameOverheadSeconds());                                         // 30초인 한 밤에 끌려가지 않음
        Assert.Equal(CameraCatalog.Generic.FrameOverheadSeconds, learned.FrameOverheadSeconds("ASI2600MC")); // 다른 카메라는 따로
    }

    [Fact]
    public void 지금_장비와_배운_값은_다시_켜도_남고_카메라를_바꿔도_섞이지_않는다()
    {
        var a = new EquipmentLearned(_dir);
        a.SetCurrentCamera("X-T5 (gin_X-T5)");
        a.RecordFrameOverhead(7, DateTimeOffset.Now);
        var b = new EquipmentLearned(_dir);
        Assert.Equal("X-T5", b.CurrentCamera());
        Assert.Equal("fuji-x", b.CameraModel().Id);
        Assert.Equal(7, b.FrameOverheadSeconds());
        b.SetCurrentCamera("ASI2600MC Pro");                   // 카메라를 바꿔 끼우면 그 모델로
        Assert.Equal("generic", b.CameraModel().Id);
        Assert.Equal(7, b.FrameOverheadSeconds("X-T5"));       // X-T5의 배운 값은 그대로
        Assert.Null(b.Value("mount", "X-T5", CameraLearned.FrameOverhead)); // 종류가 다르면 따로
    }

    [Fact]
    public void 장_사이_시간은_다른_일이_끼지_않은_간격만_잰다()
    {
        var gap = new FrameGap();
        var t = DateTimeOffset.Now;
        gap.Begin(t, 60);
        gap.Begin(t.AddSeconds(68), 60);   // 8초
        gap.Begin(t.AddSeconds(137), 60);  // 9초
        gap.Spoil();                       // 그 사이 자오선 반전
        gap.Begin(t.AddSeconds(600), 60);  // 버림
        Assert.Null(gap.Median);           // 아직 둘
        gap.Begin(t.AddSeconds(670), 60);  // 10초
        gap.Begin(t.AddSeconds(1000), 60); // 270초 — 기다린 것, 버림
        Assert.Equal([8.0, 9, 10], gap.Samples);
        Assert.Equal(9, gap.Median);
    }
}
