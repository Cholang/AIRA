using System.IO.Compression;
using System.Text;

namespace Astro.Server.Setup;

/// <summary>
/// 설정 화면의 "진단 내보내기" (2026-10-11): 문제가 생겼을 때 넘겨줄 기록을 한 파일(zip)로 바탕 화면에 묶는다.
/// 아이라 기록(최근 3일) · N.I.N.A. 최근 기록 1개 · PHD2 오늘 기록 · 버전·설정 요약. AI 키는 넣지 않는다
/// </summary>
public static class Diagnostics
{
    public static string LogsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Astro.Core.Product.DataFolder, "logs");

    public static string Export(UserSettings settings)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"{Astro.Core.Product.Id}-진단-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        // 아이라 기록: 최근 3일 (aira-날짜.log · connect-날짜.log)
        if (Directory.Exists(LogsDir))
            foreach (var f in Directory.EnumerateFiles(LogsDir, "*.log").Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddDays(-3)))
                Add(zip, f, "aira/" + Path.GetFileName(f));
        // N.I.N.A.: 가장 최근 기록 하나
        var ninaLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");
        if (Directory.Exists(ninaLogs) && new DirectoryInfo(ninaLogs).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTime).FirstOrDefault() is { } nina)
            Add(zip, nina.FullName, "nina/" + nina.Name);
        // PHD2: 오늘 기록 (문서\PHD2)
        var phd2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PHD2");
        if (Directory.Exists(phd2))
            foreach (var f in new DirectoryInfo(phd2).EnumerateFiles("PHD2_*Log_*.txt").Where(f => f.LastWriteTime > DateTime.Now.AddDays(-1)))
                Add(zip, f.FullName, "phd2/" + f.Name);
        var info = new StringBuilder()
            .AppendLine($"{Astro.Core.Product.Name} {System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Diagnostics).Assembly)?.InformationalVersion}")
            .AppendLine($"만든 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Windows: {Environment.OSVersion}")
            .AppendLine($"N.I.N.A.: {InstallLocator.NinaExe(null) ?? "찾지 못함"}{(settings.NinaExe is null ? " (자동)" : " (설정에서 지정)")}")
            .AppendLine($"SharpCap: {settings.SharpCapExe ?? Prepare.Real.SharpCapBridge.FindExe() ?? "찾지 못함"}{(settings.SharpCapExe is null ? " (자동)" : " (설정에서 지정)")}")
            .AppendLine($"AI 비서: {(settings.AiEnabled ? "켜짐" : "꺼짐")} · 설정 화면 키 {(settings.GeminiKey is null ? "없음" : "있음(내용은 넣지 않음)")}");
        using (var w = new StreamWriter(zip.CreateEntry("정보.txt").Open(), Encoding.UTF8)) w.Write(info.ToString());
        return path;
    }

    /// <summary>쓰는 중인 기록 파일도 읽을 수 있게 공유 모드로 연다</summary>
    private static void Add(ZipArchive zip, string file, string name)
    {
        try
        {
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }
        catch (IOException) { /* 못 읽는 파일은 건너뛴다 */ }
        catch (UnauthorizedAccessException) { }
    }
}
