using FluentAssertions;
using HW4NoteKeeperEx1.Controllers;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Models;
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
    /// Unit tests for zip attachment controllers.
    /// Verifies correct queue routing: old POST → legacy queue (no job row),
    /// new POST → Ex1 queue (with job row).
    /// </summary>
    public class NoteKeeperZipAttachmentControllerTests : IDisposable
    {
        private readonly MyDatabaseContext _context;
        private readonly Mock<AzureStorageService> _mockStorage;
        private readonly Mock<JobsTableService> _mockJobsTable;
        private readonly TelemetryClient _telemetryClient;
        private readonly Guid _existingNoteId;

        public NoteKeeperZipAttachmentControllerTests()
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

            // AzureStorageService and JobsTableService are not easily constructable without real Azure
            // clients, so we mock them via their virtual methods.
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

        private static DefaultHttpContext MakeHttpContext() => new()
        {
            Request = { Scheme = "https", Host = new HostString("localhost") }
        };

        // ─── Old POST (NoteKeeperZipAttachmentController) ─────────────────────────

        /// <summary>
        /// Old POST must call EnqueueLegacyZipRequestAsync (attachment-zip-requests queue)
        /// and must NOT insert a Jobs table row.
        /// </summary>
        [Fact]
        public async Task OldPost_RequestZipCreation_CallsLegacyQueue_AndDoesNotInsertJobRow()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(3);
            _mockStorage.Setup(s => s.EnqueueLegacyZipRequestAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                        .Returns(Task.CompletedTask);

            var controller = new NoteKeeperZipAttachmentController(
                _context,
                _mockStorage.Object,
                _mockJobsTable.Object,
                _telemetryClient,
                Mock.Of<ILogger<NoteKeeperZipAttachmentController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreation(_existingNoteId.ToString());

            // Assert – returns 202
            result.Should().BeOfType<AcceptedResult>();

            // Assert – legacy queue was called once
            _mockStorage.Verify(s => s.EnqueueLegacyZipRequestAsync(
                _existingNoteId.ToString(), It.IsAny<string>()), Times.Once);

            // Assert – Ex1 queue was NOT called
            _mockStorage.Verify(s => s.EnqueueZipRequestAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // Assert – Jobs table was NOT touched
            _mockJobsTable.Verify(j => j.InsertQueuedJobAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task OldPost_RequestZipCreation_Returns202_WithLocationPointingToZipFile()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(1);
            _mockStorage.Setup(s => s.EnqueueLegacyZipRequestAsync(
                            _existingNoteId.ToString(), It.IsAny<string>()))
                        .Returns(Task.CompletedTask);

            var controller = new NoteKeeperZipAttachmentController(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreation(_existingNoteId.ToString());

            // Assert – Location points to the zip-file download endpoint (not jobs)
            var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
            accepted.Location.Should().Contain($"/notes/{_existingNoteId}/attachmentzipfiles/");
            accepted.Location.Should().EndWith(".zip");
            accepted.Location.Should().NotContain("/jobs/");
        }

        [Fact]
        public async Task OldPost_RequestZipCreation_Returns404_WhenNoteDoesNotExist()
        {
            // Arrange
            var controller = new NoteKeeperZipAttachmentController(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreation(Guid.NewGuid().ToString());

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task OldPost_RequestZipCreation_Returns400_WithInvalidGuid()
        {
            // Arrange
            var controller = new NoteKeeperZipAttachmentController(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreation("not-a-guid");

            // Assert
            result.Should().BeOfType<BadRequestResult>();
        }

        [Fact]
        public async Task OldPost_RequestZipCreation_Returns204_WhenNoAttachments()
        {
            // Arrange
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(0);

            var controller = new NoteKeeperZipAttachmentController(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreation(_existingNoteId.ToString());

            // Assert
            result.Should().BeOfType<NoContentResult>();
        }

        // ─── New POST (NoteKeeperZipAttachmentControllerEx1) ─────────────────────

        /// <summary>
        /// New Ex1 POST must call EnqueueZipRequestAsync (attachment-zip-requests-ex1 queue)
        /// AND insert a Queued row into the Jobs table.
        /// </summary>
        [Fact]
        public async Task NewPost_RequestZipCreationEx1_CallsEx1Queue_AndInsertsJobRow()
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

            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context,
                _mockStorage.Object,
                _mockJobsTable.Object,
                _telemetryClient,
                Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

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
        public async Task NewPost_RequestZipCreationEx1_Returns202_WithLocationPointingToJobsEndpoint()
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

            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – Location points to the job-status endpoint, not the zip-file download
            var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
            accepted.Location.Should().Contain($"/notes/{_existingNoteId}/attachmentzipfiles/jobs/");
            accepted.Location.Should().EndWith(".zip");
        }

        [Fact]
        public async Task NewPost_RequestZipCreationEx1_InsertsJobRow_BeforeEnqueuing()
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

            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            // Act
            await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            // Assert – insert happened before enqueue
            callOrder.Should().Equal("insert", "enqueue");
        }

        [Fact]
        public async Task NewPost_RequestZipCreationEx1_Returns404_WhenNoteDoesNotExist()
        {
            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            var result = await controller.RequestZipCreationEx1(Guid.NewGuid().ToString());

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task NewPost_RequestZipCreationEx1_Returns400_WithInvalidGuid()
        {
            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            var result = await controller.RequestZipCreationEx1("not-a-guid");

            result.Should().BeOfType<BadRequestResult>();
        }

        [Fact]
        public async Task NewPost_RequestZipCreationEx1_Returns204_WhenNoAttachments()
        {
            _mockStorage.Setup(s => s.GetBlobCountAsync(_existingNoteId.ToString()))
                        .ReturnsAsync(0);

            var controller = new NoteKeeperZipAttachmentControllerEx1(
                _context, _mockStorage.Object, _mockJobsTable.Object,
                _telemetryClient, Mock.Of<ILogger<NoteKeeperZipAttachmentControllerEx1>>())
            {
                ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() }
            };

            var result = await controller.RequestZipCreationEx1(_existingNoteId.ToString());

            result.Should().BeOfType<NoContentResult>();
        }
    }
}
