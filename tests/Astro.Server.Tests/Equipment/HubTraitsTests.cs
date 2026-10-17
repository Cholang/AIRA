using Astro.Server.Engine;
using Astro.Server.Equipment;
using Astro.Server.Equipment.Hub;
using Astro.Server.Prepare.Real;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Equipment;

/// <summary>장비 › 전원 허브 (2026-10-17): 이름으로 모델을 고르고, 허브가 없거나 아직 모르면 없는 것으로</summary>
public class HubTraitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aira-hub-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    [Theory]
    [InlineData("WandererEmpire WandererBox 1 (ASCOM)", null, "wandererbox")]
    [InlineData(null, "ASCOM.WandererBox1.Switch", "wandererbox")]
    [InlineData("Pegasus Ultimate Powerbox", "ASCOM.PegasusAstro.UPBv2.Switch", "generic")]
    [InlineData(null, null, "generic")]
    public void 허브_모델을_고른다(string? name, string? driver, string expected) =>
        Assert.Equal(expected, HubCatalog.Match(name, driver).Id);

    [Fact]
    public void WandererBox_모델은_지금까지_코드에_흩어져_있던_값과_같다()
    {
        var w = HubCatalog.WandererBox;
        Assert.Equal("1A86", w.UsbVendorId);
        Assert.Equal("ASCOM.WandererBoxEnvironment.ObservingConditions", w.WeatherDriverId);
        Assert.Equal("Automatic control", w.HeaterAutoMark);
        var app = w.App!;
        Assert.Equal(("empire", "Wanderer Empire", "startup"), (app.Id, app.Name, app.SplashTitle));
        Assert.Equal(["Wanderer Empire", "ASCOM.WandererBox1", "ASCOM.WandererBox2"], app.AllProcesses);
        Assert.Equal(@"Wanderer Astro\Wanderer Empire.exe", app.ExeUnderProgramFilesX86);
    }

    [Fact]
    public void 허브가_없거나_모르면_없는_것_있으면_그_모델()
    {
        Assert.Null(HubCatalog.Current(null));
        Assert.Same(HubCatalog.Generic, HubCatalog.Current("Pegasus UPB"));
        Assert.Same(HubCatalog.WandererBox, HubCatalog.Current("ASCOM.WandererBox1.Switch"));
        Assert.Contains(HubCatalog.WandererBox.App, HubCatalog.AllApps);
        Assert.Contains("Wanderer Empire", BackgroundWindows.HubApps);
    }

    [Fact]
    public void 연결_전_USB_확인은_허브_모델의_번호()
    {
        Assert.Equal("1A86", DevicePrecheck.UsbVendor("switch", "ASCOM.WandererBox1.Switch")?.Vid);
        Assert.Null(DevicePrecheck.UsbVendor("switch", "ASCOM.PegasusAstro.UPBv2.Switch"));
    }

    [Fact]
    public async Task 날씨_센서는_지금_허브_모델의_드라이버_없으면_읽지_않는다()
    {
        var learned = new EquipmentLearned(_dir);
        var weather = new AscomWeather(NullLogger<AscomWeather>.Instance, learned);
        Assert.Null(weather.DriverId);                                            // 아직 모름 → 없는 것으로
        learned.SetCurrent(HubCatalog.Kind, "ASCOM.WandererBox1.Switch");
        Assert.Equal(HubCatalog.WandererBox.WeatherDriverId, weather.DriverId);
        learned.SetAbsent(HubCatalog.Kind);                                       // 프로필에서 허브를 뺌
        Assert.Null(weather.DriverId);
        Assert.True(learned.IsAbsent(HubCatalog.Kind));
        learned.SetCurrent(HubCatalog.Kind, "ASCOM.PegasusAstro.UPBv2.Switch");
        Assert.Null(weather.DriverId);
        Assert.Null(await weather.ReadAsync(CancellationToken.None));            // 센서 없는 허브: 읽지 않음
    }

    [Fact]
    public void 카탈로그는_공통_약속을_지킨다()
    {
        Assert.Equal("switch", Kind<HubCatalog, HubTraits>());
        Assert.Contains(HubCatalog.Default, HubCatalog.Models);
    }

    private static string Kind<TCatalog, TModel>() where TCatalog : IEquipmentCatalog<TModel> where TModel : IEquipmentModel => TCatalog.Kind;
}
