using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astro.Server.Setup;

/// <summary>
/// 앱 전체 설정 (프로필과 상관없는 값 — 2026-10-11 사용자 결정: 설정 화면에 AI 비서 · 프로그램 경로 · 기록).
/// 데이터 폴더의 settings.json. AI 키는 Windows 사용자 계정으로만 풀 수 있게 암호화(DPAPI)해 둔다 — 파일을 복사해 가도 다른 PC·계정에서는 못 읽는다.
/// 값이 없으면 예전처럼: AI 키는 사용자 비밀 저장소·환경 변수, 프로그램은 자동으로 찾는다
/// </summary>
public sealed class UserSettings
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private Data _data;

    private sealed record Data(bool AiEnabled = true, string? GeminiKey = null, string? NinaExe = null, string? SharpCapExe = null);

    public UserSettings(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Astro.Core.Product.DataFolder);
        _file = Path.Combine(root, "settings.json");
        _data = Load();
        InstallLocator.UserNinaExe = _data.NinaExe;
    }

    /// <summary>AI 비서를 쓰는가 (끄면 계획 대화는 연습 대화 — AI에 요청하지 않음)</summary>
    public bool AiEnabled { get { lock (_gate) return _data.AiEnabled; } }
    /// <summary>설정 화면에서 넣은 Gemini 키 (없으면 null)</summary>
    public string? GeminiKey { get { lock (_gate) return Unprotect(_data.GeminiKey); } }
    /// <summary>N.I.N.A. 실행 파일을 직접 지정한 경로 (없으면 자동으로 찾음)</summary>
    public string? NinaExe { get { lock (_gate) return _data.NinaExe; } }
    /// <summary>SharpCap 실행 파일을 직접 지정한 경로 (없으면 자동으로 찾음)</summary>
    public string? SharpCapExe { get { lock (_gate) return _data.SharpCapExe; } }

    public void SetAiEnabled(bool on) => Update(d => d with { AiEnabled = on });

    /// <summary>키 저장 (빈 값이면 지운다)</summary>
    public void SetGeminiKey(string? key) => Update(d => d with { GeminiKey = string.IsNullOrWhiteSpace(key) ? null : Protect(key.Trim()) });

    public void SetNinaExe(string? path)
    {
        Update(d => d with { NinaExe = Blank(path) });
        InstallLocator.UserNinaExe = NinaExe;
    }
    public void SetSharpCapExe(string? path) => Update(d => d with { SharpCapExe = Blank(path) });

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('"');

    private void Update(Func<Data, Data> change)
    {
        lock (_gate)
        {
            _data = change(_data);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private Data Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_file)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? stored)
    {
        if (stored is null) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; } // 다른 계정·PC에서 복사해 온 값
    }
}
