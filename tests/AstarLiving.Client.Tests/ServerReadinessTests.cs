using System.Net;
// WPF ตัด System.Net.Http ออกจาก implicit usings ต้องประกาศเอง
using System.Net.Http;
using System.Text;
using AstarLiving.Client.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// การอ่านสถานะเครื่องแม่ข่ายตอนเพิ่งเปิดเครื่อง
///
/// กรณีที่สำคัญที่สุดคือ "ต่อไม่ติด" ต้องไม่ใช่ข้อผิดพลาด แต่เป็นคำตอบที่ถูกต้อง
/// เพราะเป็นสิ่งที่เกิดขึ้นทุกเช้าระหว่างที่บริการยังเริ่มไม่เสร็จ
/// ถ้าตัวนี้โยนข้อผิดพลาด หน้าเข้าสู่ระบบจะเด้งข้อความแดงใส่ผู้ใช้ทั้งที่ไม่มีอะไรเสีย
/// </summary>
public class ServerReadinessTests
{
    [Fact]
    public async Task ต่อไม่ติดต้องได้สถานะรอ_ไม่ใช่ข้อผิดพลาด()
    {
        var readiness = Build(_ => throw new HttpRequestException("connection refused"));

        var status = await readiness.ProbeAsync();

        status.State.Should().Be(ServerState.Unreachable);
        status.Message.Should().Contain("กำลังรอเครื่องแม่ข่าย");
    }

    [Fact]
    public async Task ฐานข้อมูลยังไม่ขึ้นต้องได้สถานะกำลังเริ่ม()
    {
        var readiness = Build(_ => Json(HttpStatusCode.ServiceUnavailable,
            """{"status":"degraded","database":"down","migration_state":"Ready"}"""));

        var status = await readiness.ProbeAsync();

        status.State.Should().Be(ServerState.Starting);
    }

    [Fact]
    public async Task กำลังปรับปรุงฐานข้อมูลต้องบอกผู้ใช้ให้รอ()
    {
        var readiness = Build(_ => Json(HttpStatusCode.ServiceUnavailable,
            """{"status":"degraded","database":"up","migration_state":"Migrating"}"""));

        var status = await readiness.ProbeAsync();

        status.State.Should().Be(ServerState.Migrating);
        status.Message.Should().Contain("กำลังปรับปรุงฐานข้อมูล");
    }

    [Fact]
    public async Task ปรับปรุงฐานข้อมูลไม่สำเร็จต้องให้ติดต่อผู้ดูแล()
    {
        var readiness = Build(_ => Json(HttpStatusCode.ServiceUnavailable,
            """{"status":"degraded","database":"up","migration_state":"Failed"}"""));

        var status = await readiness.ProbeAsync();

        status.State.Should().Be(ServerState.Failed);
        status.Message.Should().Contain("ติดต่อผู้ดูแลระบบ");
    }

    [Fact]
    public async Task พร้อมใช้งานต้องไม่มีข้อความรบกวนผู้ใช้()
    {
        var readiness = Build(_ => Json(HttpStatusCode.OK,
            """{"status":"ready","database":"up","migration_state":"Ready"}"""));

        var status = await readiness.ProbeAsync();

        status.State.Should().Be(ServerState.Ready);
        status.Message.Should().BeEmpty();
    }

    /// <summary>
    /// นี่คือพฤติกรรมที่ลูกค้าขอโดยตรง เปิดเครื่องแล้วเปิดโปรแกรมได้เลย
    /// บริการที่ยังเริ่มไม่เสร็จต้องถูกรอให้เอง ไม่ใช่ให้ผู้ใช้มานั่งกดซ้ำ
    /// </summary>
    [Fact]
    public async Task รอจนบริการเริ่มเสร็จแล้วไปต่อเอง()
    {
        var calls = 0;

        var readiness = Build(_ =>
        {
            calls++;

            // สองครั้งแรกยังต่อไม่ติด เหมือนตอนบริการกำลังเริ่ม
            if (calls <= 2) throw new HttpRequestException("connection refused");

            return Json(HttpStatusCode.OK,
                """{"status":"ready","database":"up","migration_state":"Ready"}""");
        });

        var seen = new List<ServerState>();
        var progress = new Progress<ServerStatus>(s => seen.Add(s.State));

        var final = await readiness.WaitAsync(progress, TimeSpan.FromSeconds(30));

        final.State.Should().Be(ServerState.Ready);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task เลิกรอเมื่อหมดเวลาแล้วยังต่อไม่ติด()
    {
        var readiness = Build(_ => throw new HttpRequestException("connection refused"));

        var final = await readiness.WaitAsync(timeout: TimeSpan.Zero);

        final.State.Should().Be(ServerState.Unreachable);
    }

    private static ServerReadiness Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond))
        {
            BaseAddress = new Uri("http://127.0.0.1:5080/"),
        };

        var api = new ApiClient(http, new ClientSettings(), new AuthSession(),
            NullLogger<ApiClient>.Instance);

        return new ServerReadiness(api, NullLogger<ServerReadiness>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
