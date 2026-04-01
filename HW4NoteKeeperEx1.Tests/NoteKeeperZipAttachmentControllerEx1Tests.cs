using FluentAssertions;
using HW4NoteKeeperEx1.Controllers;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NoteKeeperZipAttachmentControllerEx1"/>.
    /// Covers: POST (RequestZipCreationEx1), GET single job status, GET all job statuses.
    /// </summary>
    public class NoteKeeperZipAttachmentControllerEx1Tests : IDisposable
    {
        private readonly MyDatabaseContext _context;
        private readonly Mock<AzureStorageService> _mockStorage;
        private readonly Mock<JobsTableService> _mockJobsTable;
        private readonly TelemetryClient _telemetryClient;
        private readonly Guid _existingNoteId;

        public NoteKeeperZipAttachmentControllerEx1Tests()
        {
            var options = new DbContextOptionsBuilder<MyDatabaseContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _context = new MyDatabaseContext(options);

            _existingNoteId = Guid.NewGuid();
            _context.Notes.Add(new Note
            {
                Id = _existingNoteId,
                Summary = "Test Note",
                Details = "Test body"
            });
            _context.SaveChanges();

            _mockStorage = new Mock<AzureStorageService>(MockBehavior.Strict,
                null!, null!, new StorageOperationalSettings(), Mock.Of<ILogger<AzureStorageService>>());

            _mockJobsTable = new Mock<JobsTableService>(MockBehavior.Strict,
                null!, Mock.Of<ILogger<JobsTableService>>());

            _telemetryClient = new TelemetryClient(new TelemetryConfiguration { DisableTelemetry = true });
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // ─── Helper ──────────────────────────────────────────────────────────────────

        private NoteKeeperZipAttachmentControllerEx1 CreateController() =>
            new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        Request = { Scheme = "https", Host = new HostString("localhost") }
                    }
                }
            };

        // ═══════════════════════════════════════════════════════════════════════════════
        //  POST /notes/{noteId}/attachmentzipfilesex1  (RequestZipCreationEx1)
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ex1 POST must call EnqueueZipRequestAsync (attachment-zip-requests-ex1 queue)
        /// AND insert a Queued row into the Jobs table.
        /// </summary>
        [Fact]
        public async Task Post_RequestZipCreationEx1_CallsEx1Queue_AndInsertsJobRow()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(2);
            _mockStorage.Setup(s => s.EnqueueZipRequestAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                        .Returns(Task.CompletedTask);
            _mockJobsTable.Setup(j => j.InsertQueuedJobAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                          .Returns(Task.CompletedTask);

            var controller = CreateController();

            // Act
            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – returns 202
            result.Should().BeOfType<AcceptedResult>();

            // Assert – Ex1 queue was called once
            _mockStorage.Verify(s => s.EnqueueZipRequestAsync(
                _existingNoteId.ToString(), It.IsAny<string>()), Times.Once);

            // Assert – legacy queue was NOT called
            _mockStorage.Verify(s => s.EnqueueLegacyZipRequestAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // Assert – Jobs table row was inserted
            _mockJobsTable.Verify(j => j.InsertQueuedJobAsync(
                _existingNoteId.ToString(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Post_RequestZipCreationEx1_Returns202_WithLocationPointingToJobsEndpoint()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(1);
            _mockStorage.Setup(s => s.EnqueueZipRequestAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                        .Returns(Task.CompletedTask);
            _mockJobsTable.Setup(j => j.InsertQueuedJobAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                          .Returns(Task.CompletedTask);

            var controller = CreateController();

            // Act
            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – Location points to the job-status endpoint, not the zip-file download
            var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
            accepted.Location.Should().Contain($"/notes/{_existingNoteId}/attachmentzipfiles/jobs/");
            accepted.Location.Should().EndWith(".zip");
        }

        [Fact]
        public async Task Post_RequestZipCreationEx1_InsertsJobRow_BeforeEnqueuing()
        {
            // Arrange – use a CallSequence to verify order: InsertQueuedJob must happen before Enqueue
            var callOrder = new List<string>();

            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(1);
            _mockJobsTable.Setup(j => j.InsertQueuedJobAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                          .Callback(() => callOrder.Add("insert"))
                          .Returns(Task.CompletedTask);
            _mockStorage.Setup(s => s.EnqueueZipRequestAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                        .Callback(() => callOrder.Add("enqueue"))
                        .Returns(Task.CompletedTask);

            var controller = CreateController();

            // Act
            await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – insert happened before enqueue
            callOrder.Should().Equal("insert", "enqueue");
        }

        [Fact]
        public async Task Post_RequestZipCreationEx1_Returns404_WhenNoteDoesNotExist()
        {
            var controller = CreateController();

            var result = await controller.RequestZipCreationEx1(Guid.NewGuid().ToString());

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Post_RequestZipCreationEx1_Returns400_WithInvalidGuid()
        {
            var controller = CreateController();

            var result = await controller.RequestZipCreationEx1("not-a-guid");

            result.Should().BeOfType<BadRequestResult>();
        }

        [Fact]
        public async Task Post_RequestZipCreationEx1_Returns204_WhenNoAttachments()
        {
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(0);

            var controller = CreateController();

            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            result.Should().BeOfType<NoContentResult>();
        }

        /// <summary>
        /// When InsertQueuedJobAsync throws, the controller must return 500 (not 202)
        /// and must NOT enqueue a message.
        /// </summary>
        [Fact]
        public async Task Post_RequestZipCreationEx1_Returns500_WhenJobInsertFails()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(1);
            _mockJobsTable.Setup(j => j.InsertQueuedJobAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                          .ThrowsAsync(new Azure.RequestFailedException("Table auth failure"));

            var controller = CreateController();

            // Act
            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – must be 500, NOT 202
            var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
            statusResult.StatusCode.Should().Be(500);

            // Assert – queue message was NOT sent (insert failed before enqueue)
            _mockStorage.Verify(s => s.EnqueueZipRequestAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  GET /notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}  (GetJobStatus)
        // ═══════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetJobStatus_Returns200_WhenJobExists()
        {
            // Arrange
            string zipFileId = $"{Guid.NewGuid()}.zip";
            var jobEntity = new JobEntity
            {
                PartitionKey = _existingNoteId.ToString(),
                RowKey = zipFileId,
                Status = "Completed",
                StatusDetails = $"Completed: zipFileId: {zipFileId}",
                Timestamp = DateTimeOffset.UtcNow
            };

            _mockJobsTable.Setup(j => j.GetJobAsync(_existingNoteId.ToString(), zipFileId))
                          .ReturnsAsync(jobEntity);

            var controller = CreateController();

            // Act
            var result = await controller.GetJobStatus(_existingNoteId.ToString(), zipFileId);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<JobStatusResponse>().Subject;
            response.Status.Should().Be("Completed");
            response.ZipFileId.Should().Be(zipFileId);
        }

        [Fact]
        public async Task GetJobStatus_Returns404_WhenJobDoesNotExist()
        {
            _mockJobsTable.Setup(j => j.GetJobAsync(_existingNoteId.ToString(), It.IsAny<string>()))
                          .ReturnsAsync((JobEntity?)null);

            var controller = CreateController();

            var result = await controller.GetJobStatus(_existingNoteId.ToString(), "nonexistent.zip");

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetJobStatus_Returns404_WhenNoteDoesNotExist()
        {
            var controller = CreateController();

            var result = await controller.GetJobStatus(Guid.NewGuid().ToString(), "any.zip");

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetJobStatus_Returns400_WithInvalidGuid()
        {
            var controller = CreateController();

            var result = await controller.GetJobStatus("not-a-guid", "any.zip");

            result.Should().BeOfType<BadRequestResult>();
        }

        /// <summary>
        /// When GetJobAsync throws, the controller must return 500.
        /// </summary>
        [Fact]
        public async Task GetJobStatus_Returns500_WhenTableServiceThrows()
        {
            _mockJobsTable.Setup(j => j.GetJobAsync(_existingNoteId.ToString(), It.IsAny<string>()))
                          .ThrowsAsync(new Azure.RequestFailedException("Table read failure"));

            var controller = CreateController();

            var result = await controller.GetJobStatus(_existingNoteId.ToString(), "any.zip");

            var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
            statusResult.StatusCode.Should().Be(500);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  GET /notes/{noteId}/attachmentzipfiles/jobs  (GetAllJobStatuses)
        // ═══════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetAllJobStatuses_Returns200_WithJobList()
        {
            // Arrange
            var jobs = new List<JobEntity>
            {
                new JobEntity { PartitionKey = _existingNoteId.ToString(), RowKey = "a.zip",
                    Status = "Completed", StatusDetails = "Done", Timestamp = DateTimeOffset.UtcNow },
                new JobEntity { PartitionKey = _existingNoteId.ToString(), RowKey = "b.zip",
                    Status = "InProgress", StatusDetails = "Working", Timestamp = DateTimeOffset.UtcNow }
            };

            _mockJobsTable.Setup(j => j.GetJobsByNoteIdAsync(_existingNoteId.ToString()))
                          .ReturnsAsync(jobs);

            var controller = CreateController();

            // Act
            var result = await controller.GetAllJobStatuses(_existingNoteId.ToString());

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var list = ok.Value.Should().BeOfType<List<JobStatusResponse>>().Subject;
            list.Should().HaveCount(2);
            list.Should().Contain(j => j.Status == "Completed");
            list.Should().Contain(j => j.Status == "InProgress");
        }

        [Fact]
        public async Task GetAllJobStatuses_Returns200_EmptyList_WhenNoJobs()
        {
            _mockJobsTable.Setup(j => j.GetJobsByNoteIdAsync(_existingNoteId.ToString()))
                          .ReturnsAsync(new List<JobEntity>());

            var controller = CreateController();

            var result = await controller.GetAllJobStatuses(_existingNoteId.ToString());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var list = ok.Value.Should().BeOfType<List<JobStatusResponse>>().Subject;
            list.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAllJobStatuses_Returns404_WhenNoteDoesNotExist()
        {
            var controller = CreateController();

            var result = await controller.GetAllJobStatuses(Guid.NewGuid().ToString());

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetAllJobStatuses_Returns400_WithInvalidGuid()
        {
            var controller = CreateController();

            var result = await controller.GetAllJobStatuses("not-a-guid");

            result.Should().BeOfType<BadRequestResult>();
        }

        /// <summary>
        /// When GetJobsByNoteIdAsync throws, the controller must return 500.
        /// </summary>
        [Fact]
        public async Task GetAllJobStatuses_Returns500_WhenTableServiceThrows()
        {
            _mockJobsTable.Setup(j => j.GetJobsByNoteIdAsync(_existingNoteId.ToString()))
                          .ThrowsAsync(new Azure.RequestFailedException("Table query failure"));

            var controller = CreateController();

            var result = await controller.GetAllJobStatuses(_existingNoteId.ToString());

            var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
            statusResult.StatusCode.Should().Be(500);
        }
    }
}
