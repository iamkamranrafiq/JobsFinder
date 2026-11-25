using System.Net.Sockets;
using JobsFinder.Application.Applying;
using JobsFinder.BrowserAutomation;
using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobsFinder.IntegrationTests.Applying;

[Collection(ApiTestGroup.Name)]
public sealed class PageLoadAndStorageTests(ApiFactory factory)
{
    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_PageNeverLoads_FailsAfterThreeRetries_F0_3_EdgePageLoad()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Failures[ApplyHost.JobPath] = (503, int.MaxValue);

        var run = await host.RunAsync(FixturePages.NewBoard());

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.PageLoad), (run.Status, run.Reason));
        Assert.Equal(4, host.Site.Hits.Count(h => h.Path == ApplyHost.JobPath));
        Assert.NotNull(await host.StoredAsync(run.Id));
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_PageLoadsOnThirdAttempt_IsPrepared_F0_3_EdgePageLoad()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Failures[ApplyHost.JobPath] = (500, 2);

        var run = await host.RunAsync(FixturePages.NewBoard());

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.Equal(3, host.Spy.OpenCalls);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(403)]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_PageIsGone_IsNotRetried_F0_3_EdgePageLoad(int status)
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Failures[ApplyHost.JobPath] = (status, int.MaxValue);

        var run = await host.RunAsync(FixturePages.NewBoard());

        Assert.Equal(ApplyReason.PageLoad, run.Reason);
        Assert.Equal(1, host.Spy.OpenCalls);
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_ConnectionRefused_FailsAfterThreeRetries_F0_3_EdgePageLoad()
    {
        await using var host = await ApplyHost.StartAsync(factory, browser: new BrowserSettings
        {
            Dial = (_, _) => throw new SocketException((int)SocketError.ConnectionRefused),
        });

        var run = await host.RunAsync(FixturePages.NewBoard());

        Assert.Equal(ApplyReason.PageLoad, run.Reason);
        Assert.Equal(4, host.Spy.OpenCalls);
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_PageHangs_FailsAsPageLoad_F0_3_EdgePageLoad()
    {
        await using var site = await FixtureSite.StartAsync();
        await using var host = await ApplyHost.StartAsync(factory, browser: new BrowserSettings
        {
            AcceptAnyCertificate = true,
            LoadTimeout = TimeSpan.FromSeconds(1),
            Dial = site.DialAsync,
        });

        var run = await host.RunUrlAsync(FixtureSite.Host + "/hang");

        Assert.Equal(ApplyReason.PageLoad, run.Reason);
        Assert.Equal(4, host.Spy.OpenCalls);
    }

    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public async Task HandleAsync_RunTakesTooLong_EndsWithinTheBudgetAndFreesTheBrowser_F0_3_NFR()
    {
        await using var host = await ApplyHost.StartAsync(factory, new ApplySettings { Budget = TimeSpan.FromSeconds(14) });
        var started = TimeProvider.System.GetTimestamp();

        // The field blocks the page for good as soon as it is typed in.
        var run = await host.RunAsync(FixturePages.NewBoard(
            script: "document.getElementById('email').addEventListener('input', () => { for (;;) { } });"));

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.TimedOut), (run.Status, run.Reason));
        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(30));
        Assert.Contains(run.Fields, f => f.Label == "First Name*");

        // Two runs in a row: the browser slot was given back.
        Assert.Equal(ApplyReason.TimedOut, (await host.RunAsync(FixturePages.Page("<form id=\"application-form\"></form>", "for (;;) { }"))).Reason);
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_Cancelled_StoresNothing_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Pages[ApplyHost.JobPath] = FixturePages.NewBoard(script: "document.getElementById('email').addEventListener('input', () => { for (;;) { } });");
        var postingId = await host.SeedAsync(FixtureSite.Host + ApplyHost.JobPath);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<PrepareApplicationHandler>()
                .HandleAsync(new PrepareApplicationCommand(postingId), cancel.Token));

        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ApplyRuns.AnyAsync(r => r.JobPostingId == postingId));
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task DeletingTheCv_DeletesItsRunsAndScreenshots_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        var run = await host.RunAsync(FixturePages.NewBoard());
        Assert.NotEmpty(await host.ScreenshotsAsync(run.Id));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cvId = await db.CandidateProfiles.Where(p => p.Id == run.CandidateProfileId).Select(p => p.CvDocumentId).SingleAsync();
        await db.Set<CvDocument>().Where(d => d.Id == cvId).ExecuteDeleteAsync();

        Assert.Null(await host.StoredAsync(run.Id));
        Assert.Empty(await host.ScreenshotsAsync(run.Id));
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task Migration_DownThenUp_OnAThrowawayDatabase_F0_3_AC1()
    {
        var shared = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:JobsFinder"]!;
        var connection = new NpgsqlConnectionStringBuilder(shared) { Database = $"apply_{Guid.NewGuid():N}", Pooling = false }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        try
        {
            var all = db.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddApplyRuns", StringComparison.Ordinal));
            var migrator = db.GetService<IMigrator>();

            await migrator.MigrateAsync(all[index]);
            Assert.Equal(2, await CountTablesAsync(db));

            await migrator.MigrateAsync(all[index - 1]);
            Assert.Equal(0, await CountTablesAsync(db));

            await migrator.MigrateAsync(all[index]);
            Assert.Equal(2, await CountTablesAsync(db));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Task<int> CountTablesAsync(AppDbContext db) =>
        db.Database
            .SqlQueryRaw<int>(
                "select count(*)::int as \"Value\" from information_schema.tables where table_name in ('apply_runs', 'apply_screenshots')")
            .SingleAsync();
}
