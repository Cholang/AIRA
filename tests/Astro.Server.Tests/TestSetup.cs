using System.Runtime.CompilerServices;

namespace Astro.Server.Tests;

/// <summary>테스트 전체에 한 번: 실제 아이라 기록 파일(데이터 폴더 logs)에 쓰지 않는다 — 진단 묶음에 테스트의 가짜 알림이 섞이지 않게</summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init() => Astro.Server.Engine.FileLogProvider.Enabled = false;
}
