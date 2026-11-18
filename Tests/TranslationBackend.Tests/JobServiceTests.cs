using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Application.Services;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Infrastructure.Data;
using TranslationBackend.Infrastructure.Repositories;
using TranslationBackend.Infrastructure.Storage;
using TranslationBackend.Utils;

namespace TranslationBackend.Tests;

public class JobServiceTests : IDisposable
{
    private readonly AppDbContext _db = TestData.NewContext();
    private readonly LocalFileStorage _storage;
    private readonly string _root;
    private readonly FakeExtractionClient _extraction = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeAudit _audit = new();
    private readonly JobOptions _options = new() { MaxUploadBytes = 1024 * 1024, MaxActivePerUser = 2, RetentionDays = 30 };
    private readonly JobService _service;
    private static readonly JobCaller Amna = new("u-amna", "amna", false);
    private static readonly JobCaller Omar = new("u-omar", "omar", false);
    private static readonly JobCaller Developer = new("u-dev", "dev", true);

    public JobServiceTests()
    {
        _storage = TestData.NewStorage(out _root);
        _service = new JobService(new JobRepo(_db), _extraction, _storage, _audit, _notifier,
            Options.Create(_options), NullLogger<JobService>.Instance);

        // the job queries join the owner, so the owners must exist, as they do in the real database
        foreach (var caller in new[] { Amna, Omar, Developer })
            _db.Users.Add(new TranslationBackend.Domain.Model.Users
            {
                Id = caller.UserId, UserName = caller.UserName, Name = caller.UserName, Email = caller.UserName + "@avanceon.ae",
            });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _db.Dispose();
    }

    private Task<TranslationBackend.Application.DTOs.JobDto> Upload(JobCaller who, string name = "report.pdf", byte[]? bytes = null,
        string language = "ru", bool verify = true)
    {
        bytes ??= name.EndsWith(".docx") ? TestData.Docx() : TestData.Pdf();
        return _service.CreateAsync(who, name, bytes.Length, new MemoryStream(bytes), language, verify);
    }

    private static async Task<JobException> Refused(Task task)
        => await Assert.ThrowsAsync<JobException>(() => task);

    // ------------------------------------------------------------------ create
    [Fact]
    public async Task A_valid_upload_is_stored_and_started_with_paths_relative_to_the_storage_root()
    {
        var job = await Upload(Amna);

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal("pdf", job.InputType);
        var started = Assert.Single(_extraction.Started);
        Assert.Equal(job.Id.ToString("D"), started.JobId);
        Assert.Equal($"{job.Id:D}/input/source.pdf", started.InputPath);
        Assert.Equal($"{job.Id:D}/output", started.OutputDir);
        Assert.True(File.Exists(_storage.Resolve(started.InputPath)));
        Assert.Contains(_audit.Actions, a => a.Contains("report.pdf"));
    }

    [Fact]
    public async Task Only_pdf_and_docx_files_are_accepted()
    {
        var error = await Refused(Upload(Amna, "notes.txt", new byte[] { 1, 2, 3 }));
        Assert.Equal("UNSUPPORTED_FILE_TYPE", error.Code);
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(_extraction.Started);
    }

    [Fact]
    public async Task Empty_and_oversized_files_are_refused()
    {
        Assert.Equal("EMPTY_FILE", (await Refused(_service.CreateAsync(Amna, "a.pdf", 0, new MemoryStream(), "ru", true))).Code);
        var big = await Refused(_service.CreateAsync(Amna, "a.pdf", _options.MaxUploadBytes + 1, new MemoryStream(TestData.Pdf()), "ru", true));
        Assert.Equal((413, "FILE_TOO_LARGE"), (big.StatusCode, big.Code));
    }

    [Fact]
    public async Task The_content_must_match_the_extension_and_nothing_is_kept_of_a_refused_file()
    {
        var notPdf = await Refused(Upload(Amna, "fake.pdf", System.Text.Encoding.ASCII.GetBytes("hello world")));
        Assert.Equal("INVALID_FILE", notPdf.Code);

        var notWord = await Refused(Upload(Amna, "fake.docx", TestData.Docx(withDocument: false)));
        Assert.Equal("INVALID_FILE", notWord.Code);

        Assert.Empty(Directory.GetDirectories(_root));            // the refused uploads were deleted again
        Assert.Empty(_extraction.Started);
    }

    [Fact]
    public async Task A_docx_with_a_real_document_part_is_accepted()
    {
        var job = await Upload(Amna, "procedure.docx");
        Assert.Equal("docx", job.InputType);
    }

    [Fact]
    public async Task Only_russian_and_kazakh_are_offered()
    {
        Assert.Equal("UNSUPPORTED_LANGUAGE", (await Refused(Upload(Amna, language: "fr"))).Code);
        Assert.Equal("kk", (await Upload(Amna, language: "KK")).Language);
    }

    [Fact]
    public async Task A_user_cannot_have_more_translations_running_than_allowed()
    {
        await Upload(Amna);
        await Upload(Amna);
        var third = await Refused(Upload(Amna));
        Assert.Equal((429, "TOO_MANY_ACTIVE_JOBS"), (third.StatusCode, third.Code));
        await Upload(Omar);                                         // another user is not affected
    }

    [Fact]
    public async Task When_the_extraction_service_is_down_the_job_is_marked_failed_and_the_user_told()
    {
        _extraction.Unavailable = true;
        var error = await Refused(Upload(Amna));
        Assert.Equal((503, "SERVICE_UNAVAILABLE"), (error.StatusCode, error.Code));

        var failed = Assert.Single((await _service.ListMineAsync(Amna.UserId, new())).Items);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.NotNull(failed.Error);
    }

    // ------------------------------------------------------------------ progress
    [Fact]
    public async Task Progress_and_the_finished_result_are_copied_from_the_extraction_service()
    {
        var job = await Upload(Amna);
        var id = job.Id.ToString("D");

        _extraction.Jobs[id] = FakeExtractionClient.State(id, ExtractionJobStatus.Running, 42, "Translating");
        Assert.True(await _service.RefreshAsync(job.Id));
        var running = (await _service.GetAsync(Amna, job.Id))!;
        Assert.Equal((JobStatus.Running, 42, "Translating"), (running.Status, running.Progress, running.Stage));
        Assert.False(await _service.RefreshAsync(job.Id));          // nothing new: no write, no push

        File.WriteAllText(Path.Combine(_root, id, "output.docx"), "x");
        Directory.CreateDirectory(Path.Combine(_root, id, "output"));
        File.WriteAllText(Path.Combine(_root, id, "output", "report_ru_replica.docx"), "translated");
        File.WriteAllText(Path.Combine(_root, id, "output", "report_ru_review.docx"), "review");
        _extraction.Finish(id, Path.Combine(_root, id, "output", "report_ru_replica.docx"), Path.Combine(_root, id, "output", "report_ru_review.docx"));
        Assert.True(await _service.RefreshAsync(job.Id));

        var done = (await _service.GetAsync(Amna, job.Id))!;
        Assert.Equal((JobStatus.Done, 100, 3, true), (done.Status, done.Progress, done.ModerateCount, done.ReviewRequired));
        Assert.Equal(95.5, done.DocumentScore);
        Assert.True(done.CanDownload && done.CanDownloadReview);
        Assert.NotNull(done.FinishedAt);
        Assert.Contains(_notifier.Toasts, t => t.Title == "Translation ready");
        Assert.Contains("job:Done:100", _notifier.Events);
    }

    [Fact]
    public async Task A_finished_job_whose_file_is_outside_the_shared_folder_fails_with_a_clear_message()
    {
        var job = await Upload(Amna);
        var id = job.Id.ToString("D");
        _extraction.Finish(id, Path.Combine(Path.GetTempPath(), "somewhere-else", "out.docx"));

        await _service.RefreshAsync(job.Id);

        var failed = (await _service.GetAsync(Amna, job.Id))!;
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Contains("Storage:Root", failed.Error);
    }

    [Fact]
    public async Task A_job_the_extraction_service_does_not_know_fails_once_it_is_not_brand_new()
    {
        var job = await Upload(Amna);
        _extraction.Jobs.Clear();

        Assert.False(await _service.RefreshAsync(job.Id));          // too young: the service may simply not have it yet
        Assert.Equal(JobStatus.Queued, (await _service.GetAsync(Amna, job.Id))!.Status);

        var row = await _db.TranslationJobs.FindAsync(job.Id);
        row!.CreatedAt = DateTimeHelper.Now.AddMinutes(-10);
        await _db.SaveChangesAsync();

        Assert.True(await _service.RefreshAsync(job.Id));
        Assert.Equal(JobStatus.Failed, (await _service.GetAsync(Amna, job.Id))!.Status);
    }

    [Fact]
    public async Task An_unreachable_service_is_reported_to_the_caller_so_the_poller_can_wait()
    {
        var job = await Upload(Amna);
        _extraction.Unavailable = true;
        await Assert.ThrowsAsync<ExtractionUnavailableException>(() => _service.RefreshAsync(job.Id));
        Assert.Equal(JobStatus.Queued, (await _service.GetAsync(Amna, job.Id))!.Status);

        await _service.FailAsync(job.Id, "gone");
        var failed = (await _service.GetAsync(Amna, job.Id))!;
        Assert.Equal((JobStatus.Failed, "gone"), (failed.Status, failed.Error));
    }

    // ------------------------------------------------------------------ who may see what
    [Fact]
    public async Task A_user_sees_only_their_own_jobs_and_a_developer_sees_all()
    {
        var job = await Upload(Amna);

        Assert.NotNull(await _service.GetAsync(Amna, job.Id));
        Assert.Null(await _service.GetAsync(Omar, job.Id));
        Assert.NotNull(await _service.GetAsync(Developer, job.Id));
        Assert.Empty((await _service.ListMineAsync(Omar.UserId, new())).Items);
        Assert.Single((await _service.ListMineAsync(Amna.UserId, new())).Items);
        Assert.Null(await _service.CancelAsync(Omar, job.Id));
        Assert.Null(await _service.GetFileAsync(Omar, job.Id, JobFileKind.Output));
    }

    [Fact]
    public async Task The_history_lists_every_job_and_filters_by_user_status_and_text()
    {
        await Upload(Amna, "alpha.pdf");
        await Upload(Omar, "beta.pdf");

        Assert.Equal(2, (await _service.HistoryAsync(new())).TotalCount);
        Assert.Single((await _service.HistoryAsync(new() { UserId = Omar.UserId })).Items);
        Assert.Single((await _service.HistoryAsync(new() { Search = "alpha" })).Items);
        Assert.Empty((await _service.HistoryAsync(new() { Status = JobStatus.Done })).Items);
    }

    // ------------------------------------------------------------------ cancel
    [Fact]
    public async Task A_running_job_can_be_cancelled_and_a_finished_one_cannot()
    {
        var job = await Upload(Amna);
        var cancelled = (await _service.CancelAsync(Amna, job.Id))!;
        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
        Assert.Contains(job.Id.ToString("D"), _extraction.Cancelled);

        var again = await Assert.ThrowsAsync<JobException>(() => _service.CancelAsync(Amna, job.Id));
        Assert.Equal((409, "JOB_FINISHED"), (again.StatusCode, again.Code));
    }

    // ------------------------------------------------------------------ files
    [Fact]
    public async Task Files_are_served_only_when_the_job_is_done_and_not_expired()
    {
        var job = await Upload(Amna, "Quarterly report.pdf");
        var id = job.Id.ToString("D");

        var early = await Assert.ThrowsAsync<JobFileException>(() => _service.GetFileAsync(Amna, job.Id, JobFileKind.Output));
        Assert.Equal((409, "NOT_READY"), (early.StatusCode, early.Code));

        Directory.CreateDirectory(Path.Combine(_root, id, "output"));
        var output = Path.Combine(_root, id, "output", "r_ru_replica.docx");
        File.WriteAllText(output, "translated");
        _extraction.Finish(id, output);
        await _service.RefreshAsync(job.Id);

        var file = (await _service.GetFileAsync(Amna, job.Id, JobFileKind.Output))!;
        Assert.Equal("Quarterly report_RU.docx", file.DownloadName);
        Assert.Equal("translated", File.ReadAllText(file.FullPath));

        var noReview = await Assert.ThrowsAsync<JobFileException>(() => _service.GetFileAsync(Amna, job.Id, JobFileKind.Review));
        Assert.Equal(410, noReview.StatusCode);                     // this run produced no review file

        File.Delete(output);
        var gone = await Assert.ThrowsAsync<JobFileException>(() => _service.GetFileAsync(Amna, job.Id, JobFileKind.Output));
        Assert.Equal("FILE_MISSING", gone.Code);
    }

    [Fact]
    public async Task Files_of_old_finished_jobs_are_deleted_but_the_history_row_stays()
    {
        var job = await Upload(Amna);
        var id = job.Id.ToString("D");
        var output = Path.Combine(_root, id, "output", "r.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "x");
        _extraction.Finish(id, output);
        await _service.RefreshAsync(job.Id);

        Assert.Equal(0, await _service.DeleteExpiredFilesAsync(30));            // finished just now

        var row = await _db.TranslationJobs.FindAsync(job.Id);
        row!.FinishedAt = DateTimeHelper.Now.AddDays(-40);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _service.DeleteExpiredFilesAsync(30));
        Assert.False(Directory.Exists(Path.Combine(_root, id)));
        var after = (await _service.GetAsync(Amna, job.Id))!;
        Assert.True(after.FilesExpired);
        Assert.False(after.CanDownload);
        var expired = await Assert.ThrowsAsync<JobFileException>(() => _service.GetFileAsync(Amna, job.Id, JobFileKind.Output));
        Assert.Equal((410, "FILES_EXPIRED"), (expired.StatusCode, expired.Code));
    }
}
