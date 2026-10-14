using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Real;

/// <summary>시험 사진을 같은 날짜 폴더의 "시험" 폴더로 옮기기 (2026-10-14)</summary>
public class ImageFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aa-folders-" + Guid.NewGuid().ToString("N"));
    private const string Name = "2026-10-14_21-00-00___120.00s_0000.fits";

    public ImageFoldersTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Put(string dir, DateTime? written = null)
    {
        var d = Path.Combine(_root, dir);
        Directory.CreateDirectory(d);
        var f = Path.Combine(d, Name);
        File.WriteAllText(f, "x");
        if (written is { } w) File.SetLastWriteTime(f, w);
        return f;
    }

    [Fact]
    public void 종류_폴더면_같은_날짜_폴더의_시험_폴더로()
    {
        var f = Put(@"2026-10-14\SNAPSHOT");
        var moved = ImageFolders.Move(f, "SNAPSHOT", ImageFolders.Test);
        Assert.Equal(Path.Combine(_root, "2026-10-14", "시험", Name), moved);
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void 종류_폴더가_아니면_그_아래_시험_폴더로()
    {
        var f = Put(@"M31\2026-10-14");
        Assert.Equal(Path.Combine(_root, "M31", "2026-10-14", "시험", Name), ImageFolders.Move(f, "SNAPSHOT", ImageFolders.Test));
    }

    [Fact]
    public void 같은_이름이_있으면_덮지_않고_실패()
    {
        var f = Put(@"2026-10-14\SNAPSHOT");
        Put(@"2026-10-14\시험");
        Assert.ThrowsAny<IOException>(() => ImageFolders.Move(f, "SNAPSHOT", ImageFolders.Test));
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void 옮긴_뒤에도_이름으로_찾는다()
    {
        var written = DateTime.Now;
        var f = Put(@"2026-10-14\SNAPSHOT", written);
        var moved = ImageFolders.Move(f, "SNAPSHOT", ImageFolders.Test);
        Assert.Equal(moved, NinaRig.FindSavedFile(_root, Name, written));
    }
}
