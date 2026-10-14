namespace Astro.Server.Prepare.Real;

/// <summary>
/// N.I.N.A.가 저장한 사진을 같은 날짜 폴더의 다른 폴더로 옮긴다: …\날짜\SNAPSHOT\파일 → …\날짜\시험\파일.
/// 저장 이름 규칙이 종류 폴더(LIGHT·DARK·SNAPSHOT…)를 쓰지 않으면 같은 폴더 아래로. 같은 이름이 이미 있으면 덮지 않고 IOException.
/// 옮겨도 이미지 폴더 아래라 NinaRig.FindSavedFile(이름으로 찾기)은 그대로 찾는다
/// </summary>
public static class ImageFolders
{
    /// <summary>시험 사진 폴더 (OPERATION_PROCEDURE.md ⑦ 시험 사진)</summary>
    public const string Test = "시험";

    /// <summary>옮긴 경로를 돌려준다. 옮기지 못하면 IOException·UnauthorizedAccessException</summary>
    public static string Move(string file, string typeFolder, string target)
    {
        var dir = Path.GetDirectoryName(file) ?? "";
        var to = string.Equals(Path.GetFileName(dir), typeFolder, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(dir) ?? dir, target) : Path.Combine(dir, target);
        Directory.CreateDirectory(to);
        var dest = Path.Combine(to, Path.GetFileName(file));
        File.Move(file, dest, overwrite: false);
        return dest;
    }
}
