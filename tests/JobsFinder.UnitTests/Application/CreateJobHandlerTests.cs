using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Jobs;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.UnitTests.Application;

public class CreateJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeJobRepository _jobs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedTimeProvider _time = new(Now);

    private CreateJobHandler CreateSut() => new(_jobs, _unitOfWork, new CreateJobValidator(_time), _time);

    private static CreateJobCommand ValidCommand() =>
        new("Senior .NET Developer", "Acme", "Remote, worldwide", RemotePolicy.Remote, new Uri("https://example.com/jobs/1"), Now.AddDays(-2));

    [Fact]
    public async Task HandleAsync_ValidCommand_AddsJobAndSaves()
    {
        var result = await CreateSut().HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_jobs.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal("Senior .NET Developer", result.Value!.Title);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ReturnsValidationErrorWithoutSaving()
    {
        var command = ValidCommand() with { Title = "", Url = new Uri("ftp://example.com/x") };

        var result = await CreateSut().HandleAsync(command, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains(nameof(CreateJobCommand.Title), result.Error.Details!.Keys);
        Assert.Contains(nameof(CreateJobCommand.Url), result.Error.Details.Keys);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_DuplicateUrl_ReturnsConflict()
    {
        _jobs.ExistingUrls.Add(ValidCommand().Url);

        var result = await CreateSut().HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Empty(_jobs.Added);
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public List<Job> Added { get; } = [];
        public HashSet<Uri> ExistingUrls { get; } = [];

        public Task<JobDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.FirstOrDefault(j => j.Id == id)?.ToDto());

        public Task<PagedResult<JobDto>> SearchAsync(SearchJobsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<JobDto>([.. Added.Select(j => j.ToDto())], query.Page, query.PageSize, Added.Count));

        public Task<bool> ExistsByUrlAsync(Uri url, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingUrls.Contains(url));

        public void Add(Job job) => Added.Add(job);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
