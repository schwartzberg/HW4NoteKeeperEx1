using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JobsTableService"/> covering insert, read, and delete
    /// operations against a mocked <see cref="TableClient"/>.
    /// </summary>
    public class JobsTableServiceTests
    {
        private readonly Mock<TableClient> _mockTableClient;
        private readonly JobsTableService _service;

        public JobsTableServiceTests()
        {
            _mockTableClient = new Mock<TableClient>();
            var logger = Mock.Of<ILogger<JobsTableService>>();
            _service = new JobsTableService(_mockTableClient.Object, logger);
        }

        /// <summary>
        /// Verifies that InsertQueuedJobAsync calls AddEntityAsync with correct
        /// PartitionKey, RowKey, Status, and StatusDetails, then that
        /// DeleteJobsByNoteIdAsync removes the row via DeleteEntityAsync.
        /// </summary>
        [Fact]
        public async Task InsertAndDelete_JobRow_CallsTableClientCorrectly()
        {
            // Arrange
            string noteId = Guid.NewGuid().ToString();
            string zipFileId = $"{Guid.NewGuid()}.zip";
            string normalizedNoteId = noteId.ToLowerInvariant();

            // Capture the entity passed to AddEntityAsync
            JobEntity? capturedEntity = null;
            _mockTableClient
                .Setup(tc => tc.AddEntityAsync(It.IsAny<JobEntity>(), default))
                .Callback<JobEntity, CancellationToken>((entity, _) => capturedEntity = entity)
                .ReturnsAsync(Mock.Of<Response>());

            // Act – Insert
            await _service.InsertQueuedJobAsync(noteId, zipFileId);

            // Assert – Insert
            capturedEntity.Should().NotBeNull();
            capturedEntity!.PartitionKey.Should().Be(normalizedNoteId);
            capturedEntity.RowKey.Should().Be(zipFileId);
            capturedEntity.Status.Should().Be("Queued");
            capturedEntity.StatusDetails.Should().Contain(zipFileId);
            capturedEntity.StatusDetails.Should().Contain(normalizedNoteId);

            _mockTableClient.Verify(
                tc => tc.AddEntityAsync(It.IsAny<JobEntity>(), default),
                Times.Once);

            // Arrange – Delete: QueryAsync must return the inserted entity
            var page = Page<JobEntity>.FromValues(
                new[] { capturedEntity },
                continuationToken: null,
                Mock.Of<Response>());

            var pageable = AsyncPageable<JobEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(tc => tc.QueryAsync<JobEntity>(
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<IEnumerable<string>>(),
                    default))
                .Returns(pageable);

            _mockTableClient
                .Setup(tc => tc.DeleteEntityAsync(normalizedNoteId, zipFileId, default, default))
                .ReturnsAsync(Mock.Of<Response>());

            // Act – Delete
            int deletedCount = await _service.DeleteJobsByNoteIdAsync(noteId);

            // Assert – Delete
            deletedCount.Should().Be(1);
            _mockTableClient.Verify(
                tc => tc.DeleteEntityAsync(normalizedNoteId, zipFileId, default, default),
                Times.Once);
        }

        /// <summary>
        /// Verifies that DeleteJobsByNoteIdAsync returns 0 when no rows exist.
        /// </summary>
        [Fact]
        public async Task DeleteJobs_NoRows_ReturnsZero()
        {
            // Arrange – empty query result
            var emptyPage = Page<JobEntity>.FromValues(
                Array.Empty<JobEntity>(),
                continuationToken: null,
                Mock.Of<Response>());

            _mockTableClient
                .Setup(tc => tc.QueryAsync<JobEntity>(
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<IEnumerable<string>>(),
                    default))
                .Returns(AsyncPageable<JobEntity>.FromPages(new[] { emptyPage }));

            // Act
            int deletedCount = await _service.DeleteJobsByNoteIdAsync("nonexistent-note");

            // Assert
            deletedCount.Should().Be(0);
            _mockTableClient.Verify(
                tc => tc.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), default, default),
                Times.Never);
        }
    }
}
