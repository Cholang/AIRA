using Astro.Server.Engine;
using Astro.Server.Equipment;
using Astro.Server.Equipment.Camera;
using Astro.Server.Equipment.Focuser;
using Astro.Server.Equipment.GuideCamera;

namespace Astro.Server.Tests.Equipment;

/// <summary>장비 › 포커서 · 가이드 카메라 (2026-10-17): 이름으로 모델을 고르고, 예전 코드에 흩어져 있던 값과 같다</summary>
public class FocuserAndGuideCameraTests
{
    [Theory]
    [InlineData("Oasis Focuser", "ASCOM.AOFocuser.Focuser", "oasis")]   // 2026-10-17 집에서 N.I.N.A.가 알려 준 이름·드라이버
    [InlineData(null, "ASCOM.AOFocuser.Focuser", "oasis")]
    [InlineData("ZWO EAF", "ASCOM.EAF.Focuser", "generic")]
    [InlineData(null, null, "generic")]
    public void 포커서_모델을_고른다(string? name, string? driver, string expected) =>
        Assert.Equal(expected, FocuserCatalog.Match(name, driver).Id);

    [Fact]
    public void Oasis는_아이라가_0점을_잡고_모르는_포커서는_사용자가()
    {
        Assert.True(FocuserCatalog.Oasis.ZeroBySdk);
        Assert.Equal(56000, FocuserCatalog.Oasis.MaxPosition);
        Assert.Contains("Clear stall", FocuserCatalog.Oasis.StallFix);
        Assert.False(FocuserCatalog.Generic.ZeroBySdk);
        Assert.Null(FocuserCatalog.Generic.MaxPosition);
        Assert.DoesNotContain("Clear stall", FocuserCatalog.Generic.StallFix);
    }

    [Theory]
    [InlineData("G3M662M(USB2.0)", "touptek")]   // PHD2가 알려 주는 이름 (제조사가 이름에 없음)
    [InlineData("ToupTek Camera", "touptek")]
    [InlineData("ZWO ASI120MM Mini", "zwo")]
    [InlineData("QHY5III462C", "qhy")]
    [InlineData("Simulator", "generic")]
    [InlineData(null, "generic")]
    public void 가이드_카메라_모델을_고른다(string? name, string expected) =>
        Assert.Equal(expected, GuideCameraCatalog.Match(name).Id);

    [Fact]
    public void 연결_전_USB_확인은_모델의_USB_번호를_쓴다()
    {
        // 예전 DevicePrecheck 표와 같은 번호
        Assert.Equal("04CB", DevicePrecheck.UsbVendor("camera", "Fujifilm Camera Plugin_X-T5")?.Vid);
        Assert.Equal("04A9", DevicePrecheck.UsbVendor("camera", "Canon EOS (ASCOM)")?.Vid);
        Assert.Null(DevicePrecheck.UsbVendor("camera", "ZWO ASI2600MC"));
        Assert.Equal("338F", DevicePrecheck.UsbVendor("focuser", "ASCOM.AOFocuser.Focuser")?.Vid);
        Assert.Null(DevicePrecheck.UsbVendor("focuser", "ASCOM.Simulator.Focuser"));
        Assert.Equal("0547", DevicePrecheck.GuideCameraVendor("G3M662M(USB2.0)")?.Vid); // 이름에 제조사가 없어도 (새로)
        Assert.Equal("03C3", DevicePrecheck.GuideCameraVendor("ZWO ASI120MM")?.Vid);
        Assert.Null(DevicePrecheck.GuideCameraVendor("Simulator"));
    }

    [Fact]
    public void 카탈로그는_공통_약속을_지킨다()
    {
        Assert.Equal("focuser", Kind<FocuserCatalog, FocuserTraits>());
        Assert.Equal("guidecamera", Kind<GuideCameraCatalog, GuideCameraTraits>());
        Assert.Contains(FocuserCatalog.Default, FocuserCatalog.Models);
        Assert.Contains(GuideCameraCatalog.Default, GuideCameraCatalog.Models);
        Assert.Equal(CameraCatalog.Models.Count, CameraCatalog.Models.Select(m => m.Id).Distinct().Count());
    }

    private static string Kind<TCatalog, TModel>() where TCatalog : IEquipmentCatalog<TModel> where TModel : IEquipmentModel => TCatalog.Kind;
}
