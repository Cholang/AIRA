using System.Buffers.Binary;
using System.Text;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Tests.Real;

/// <summary>시험 사진의 별 모양: 별마다 이심률·방향, 아홉 칸 모으기, 아홉 칸 판정, ASTAP 별 목록 (2026-10-16)</summary>
public class StarShapesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("aa-stars-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int W = 600, H = 420;

    /// <summary>가로 9 × 세로 6 격자의 가우스 별 (배경 1000 + 잡음 20). sigma(열, 줄) → (긴 축, 짧은 축), 긴 축 방향 angle°</summary>
    private string Write(Func<int, int, (double A, double B)> sigma, double angle = 0, bool bottomUp = false)
    {
        var px = new double[W * H];
        var rnd = new Random(7);
        for (var i = 0; i < px.Length; i++) px[i] = 1000 + (rnd.NextDouble() + rnd.NextDouble() + rnd.NextDouble() - 1.5) * 40;
        var th = angle * Math.PI / 180;
        foreach (var s in Grid())
        {
            var (a, b) = sigma(s.Col, s.Row);
            for (var y = (int)s.Y - 30; y <= s.Y + 30; y++)
                for (var x = (int)s.X - 30; x <= s.X + 30; x++)
                {
                    double dx = x - s.X, dy = y - s.Y;
                    double u = dx * Math.Cos(th) + dy * Math.Sin(th), v = -dx * Math.Sin(th) + dy * Math.Cos(th);
                    px[y * W + x] += 3000 * Math.Exp(-(u * u / (2 * a * a) + v * v / (2 * b * b)));
                }
        }
        var cards = new List<string> { "SIMPLE  =                    T", "BITPIX  =                   16", "NAXIS   =                    2",
            $"NAXIS1  = {W,20}", $"NAXIS2  = {H,20}", "BZERO   =                32768" };
        if (bottomUp) cards.Add("ROWORDER= 'BOTTOM-UP'");
        cards.Add("END");
        var header = new StringBuilder();
        foreach (var card in cards) header.Append(card.PadRight(80));
        while (header.Length % 2880 != 0) header.Append(' ');
        var data = new byte[(W * H * 2 + 2879) / 2880 * 2880];
        for (var i = 0; i < W * H; i++) BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 2, 2), (short)(Math.Clamp((int)px[i], 0, 65535) - 32768));
        var file = Path.Combine(_dir, $"shot-{Guid.NewGuid():N}.fits");
        File.WriteAllBytes(file, [.. Encoding.ASCII.GetBytes(header.ToString()), .. data]);
        return file;
    }

    /// <summary>별 자리 (파일 순서, 0부터). 열 0~8, 줄 0~5 — 아홉 칸마다 별 6개</summary>
    private static IEnumerable<(int Col, int Row, double X, double Y)> Grid() =>
        from row in Enumerable.Range(0, 6) from col in Enumerable.Range(0, 9)
        select (col, row, 35 + col * 66.0 + 0.3, 35 + row * 70.0 - 0.2);

    /// <summary>ASTAP이 줄 별 목록처럼 (위치는 1부터)</summary>
    private static List<StarShapes.Star> Stars(Func<int, int, double> hfd) =>
        [.. Grid().Select(s => new StarShapes.Star(s.X + 1, s.Y + 1, hfd(s.Col, s.Row), 50))];

    [Fact]
    public void 둥근_별은_이심률이_작고_아홉_칸이_고르다()
    {
        var img = Fits.Open(Write((_, _) => (2.5, 2.5)))!;
        var r = StarShapes.Measure(img, Stars((_, _) => 5.9));
        Assert.Equal(54, r.Stars);
        Assert.InRange(r.Eccentricity!.Value, 0, 0.2);
        Assert.All(r.Field.Tiles, t => Assert.Equal(6, t.Stars));
        Assert.Equal(StarFieldRules.Kind.Even, StarFieldRules.Judge(r.Field).Kind);
        Assert.False(TestShotRules.Trailed(new ShotStats(2.9, r.Eccentricity, 0.2, 0, 0.5, "", null, r.Field)));
    }

    [Theory]
    [InlineData(1.2, 0.553)]
    [InlineData(1.6, 0.781)]
    [InlineData(2.0, 0.866)]
    public void 한쪽으로_늘어난_별은_이심률이_맞고_방향이_모인다(double ratio, double eccentricity)
    {
        var img = Fits.Open(Write((_, _) => (2.5 * ratio, 2.5), angle: 35))!;
        var r = StarShapes.Measure(img, Stars((_, _) => 5.9 * Math.Sqrt(ratio)));
        Assert.Equal(eccentricity, r.Eccentricity!.Value, 0.04); // √(1 − (짧은/긴)²)
        Assert.InRange(r.Field.Alignment, 0.9, 1);
        Assert.Equal(ratio > 1.3, TestShotRules.Trailed(new ShotStats(3, r.Eccentricity, 0.2, 0, 0.5, "", null, r.Field)));
    }

    [Fact]
    public void 길쭉해도_방향이_제각각이면_흐름으로_보지_않는다()
    {
        // 칸마다 다른 방향으로 늘어난 별 (광학·잡음처럼) — 가운데 칸 별 6개도 각각 다른 방향
        var field = new StarField([.. Enumerable.Range(0, 9).Select(_ => new StarTile(6, 6.0, 0.7))], Alignment: 0.2);
        var s = new ShotStats(3, 0.7, 0.2, 0, 0.5, "", null, field);
        Assert.False(TestShotRules.Trailed(s));
        Assert.Contains("별 모양 제각각", TestShotRules.Judge(s, 2.9, 120).Caption);
        Assert.Null(TestShotRules.Judge(s, 2.9, 120).Problem);
        // 방향을 모르면(모의 장비) 길쭉함만으로
        Assert.True(TestShotRules.Trailed(s with { Field = null }));
    }

    [Fact]
    public void 아래부터_쓴_사진은_칸의_위아래를_뒤집는다()
    {
        // 파일 첫 줄 쪽(줄 0~1) 별만 크게 → TOP-DOWN이면 위 칸, BOTTOM-UP이면 아래 칸
        double Hfd(int col, int row) => row < 2 ? 9 : 5.9;
        var top = StarShapes.Measure(Fits.Open(Write((_, _) => (2.5, 2.5)))!, Stars(Hfd));
        var bottom = StarShapes.Measure(Fits.Open(Write((_, _) => (2.5, 2.5), bottomUp: true))!, Stars(Hfd));
        Assert.Equal(9, top.Field.Tiles[1].Hfd);
        Assert.Equal(5.9, top.Field.Tiles[7].Hfd);
        Assert.Equal(9, bottom.Field.Tiles[7].Hfd);
        Assert.Equal(5.9, bottom.Field.Tiles[1].Hfd);
    }

    [Fact]
    public void 가장자리에_붙은_별은_크기만_세고_모양은_재지_않는다()
    {
        var img = Fits.Open(Write((_, _) => (2.5, 2.5)))!;
        var r = StarShapes.Measure(img, [new(3, 3, 5.9, 50), new(W - 2, H - 2, 5.9, 50)]);
        Assert.Equal(1, r.Field.Tiles[0].Stars);
        Assert.Null(r.Field.Tiles[0].Eccentricity);
        Assert.Null(r.Eccentricity);
    }

    private static StarField Field(params double[] hfd) =>
        new([.. hfd.Select(h => new StarTile(10, h, 0.4))], 0.1);

    [Fact]
    public void 아홉_칸_판정()
    {
        // 칸 순서: 왼쪽 위 · 위 · 오른쪽 위 / 왼쪽 · 가운데 · 오른쪽 / 왼쪽 아래 · 아래 · 오른쪽 아래
        Assert.Equal(StarFieldRules.Kind.Even, StarFieldRules.Judge(Field(3.1, 3, 3.2, 3, 2.9, 3, 3.1, 3, 3.0)).Kind);
        var corner = StarFieldRules.Judge(Field(3.0, 3, 4.2, 3, 2.9, 3, 3.1, 3, 3.0));
        Assert.Equal((StarFieldRules.Kind.Tilted, "오른쪽 위"), (corner.Kind, corner.Side));
        Assert.Equal("오른쪽", StarFieldRules.Judge(Field(3.0, 3, 4.2, 3, 2.9, 3, 3.1, 3, 4.0)).Side);   // 오른쪽 위·아래가 함께 큼
        Assert.Equal("아래", StarFieldRules.Judge(Field(3.0, 3, 3.1, 3, 2.9, 3, 4.1, 3, 4.0)).Side);     // 왼쪽·오른쪽 아래가 함께 큼
        Assert.Equal(StarFieldRules.Kind.Corners, StarFieldRules.Judge(Field(4.0, 3.5, 4.1, 3.5, 3.0, 3.5, 4.0, 3.5, 4.1)).Kind);
        // 귀퉁이 별이 모자라면 판정하지 않는다
        var few = Field(3, 3, 4.5, 3, 3, 3, 3, 3, 3);
        few = few with { Tiles = [.. few.Tiles.Select((t, k) => k == 2 ? t with { Stars = StarFieldRules.MinStars - 1 } : t)] };
        Assert.Equal(StarFieldRules.Kind.Unknown, StarFieldRules.Judge(few).Kind);
    }

    [Fact]
    public void 기울어짐은_검사_글에만_나오고_촬영을_막지_않는다()
    {
        var s = new ShotStats(3, 0.3, 0.2, 0, 0.5, "", null, Field(3.0, 3, 4.2, 3, 2.9, 3, 3.1, 3, 3.0));
        var v = TestShotRules.Judge(s, 2.9, 120);
        Assert.Contains("기울어짐 의심(오른쪽 위 별이 큼)", v.Caption);
        Assert.Null(v.Problem);
        Assert.Equal(StarFieldRules.Kind.Tilted, v.Field!.Kind);
    }

    [Fact]
    public void 아홉_칸_조각은_귀퉁이_변_가운데에서_같은_크기로_자르고_배경은_15퍼센트쯤()
    {
        var img = Fits.Open(Write((_, _) => (2.5, 2.5)))!;
        var tiles = TestTiles.Make(img)!;
        Assert.Equal(9, tiles.Length);
        Assert.All(tiles, t => Assert.Equal(0x89, t[0])); // PNG
        var st = TestTiles.Stretch(img);
        Assert.InRange(TestTiles.Curve(1000, st.Low, st.High, st.Mid), 0.1, 0.2); // 배경 1000(중앙값)이 15% 근처
        Assert.Equal(1.0, TestTiles.Curve(st.High, st.Low, st.High, st.Mid), 6);
        Assert.Equal(0.0, TestTiles.Curve(st.Low - 10, st.Low, st.High, st.Mid), 6);
        // 사진 420×600의 1/3 = 140 → 조각 한 변 140, 가장 밝은 별 꼭대기가 들어 있다
        var s = Math.Min(TestTiles.Side, Math.Min(W, H) / 3);
        var crop = TestTiles.Crop(img, 0, s, st);
        Assert.Equal(s * s, crop.Length);
        Assert.True(crop.Max() > 160);
    }

    [Fact]
    public void 아래부터_쓴_사진은_조각도_위아래를_뒤집는다()
    {
        // 파일 첫 줄 쪽(줄 0)의 별만 크게 → TOP-DOWN이면 위 칸, BOTTOM-UP이면 아래 칸의 조각이 밝다
        double Big(int col, int row) => row == 0 ? 5 : 1;
        var top = Fits.Open(Write((c, r) => (Big(c, r), Big(c, r))))!;
        var bottom = Fits.Open(Write((c, r) => (Big(c, r), Big(c, r)), bottomUp: true))!;
        var s = Math.Min(TestTiles.Side, Math.Min(W, H) / 3);
        static double Mean(byte[] px) => px.Average(v => (double)v);
        var stTop = TestTiles.Stretch(top);
        var stBottom = TestTiles.Stretch(bottom);
        Assert.True(Mean(TestTiles.Crop(top, 0, s, stTop)) > Mean(TestTiles.Crop(top, 6, s, stTop)));
        Assert.True(Mean(TestTiles.Crop(bottom, 6, s, stBottom)) > Mean(TestTiles.Crop(bottom, 0, s, stBottom)));
    }

    [Fact]
    public void ASTAP_별_목록을_읽는다()
    {
        var stars = StarShapes.Parse(["x,y,hfd,snr,flux,ra[0..360],dec[0..360]", "3317.2297,40.8816,8.2368,46,12533", "bad,line", "1,2,0,5,1"]);
        Assert.Equal(new StarShapes.Star(3317.2297, 40.8816, 8.2368, 46), Assert.Single(stars)); // HFD 0인 줄은 뺀다
    }

    [Fact]
    public async Task CX38_04_같은_이름의_CSV가_이미_있으면_읽지도_지우지도_않는다()
    {
        var file = Write((_, _) => (2.5, 2.5));
        var csv = Path.ChangeExtension(file, ".csv");
        File.WriteAllText(csv, "x,y,hfd,snr" + Environment.NewLine + "10,10,3,50");
        // 실행할 수 없는 경로를 줘도 실행 전에 돌아온다 — 사용자 파일은 그대로
        Assert.Null(await StarShapes.ExtractAsync(Path.Combine(_dir, "no-astap.exe"), file, CancellationToken.None));
        Assert.Equal("x,y,hfd,snr" + Environment.NewLine + "10,10,3,50", File.ReadAllText(csv));
    }

    [Fact]
    public async Task ASTAP이_있으면_사진에서_별을_찾고_목록_파일을_지운다()
    {
        if (StarShapes.CliPath(null) is not { } cli) return; // ASTAP이 설치된 PC에서만 (두 PC 모두 설치됨)
        var file = Write((_, _) => (2.5, 2.5));
        var stars = await StarShapes.ExtractAsync(cli, file, CancellationToken.None);
        Assert.NotNull(stars);
        Assert.InRange(stars.Count, 45, 60);
        Assert.False(File.Exists(Path.ChangeExtension(file, ".csv")));
        var r = StarShapes.Measure(Fits.Open(file)!, stars);
        Assert.InRange(r.Eccentricity!.Value, 0, 0.25);
    }
}
