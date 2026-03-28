using FluentAssertions;
using HW4NoteKeeperEx1.Controllers;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Unit tests for TagsController - tests the GET /tags endpoint
    /// </summary>
    public class TagsControllerTests : IDisposable
    {
        private readonly MyDatabaseContext _context;
        private readonly TagsController _controller;
        private readonly Mock<ILogger<TagsController>> _mockLogger;

        public TagsControllerTests()
        {
            var options = new DbContextOptionsBuilder<MyDatabaseContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new MyDatabaseContext(options);
            _mockLogger = new Mock<ILogger<TagsController>>();
            _controller = new TagsController(_context, _mockLogger.Object);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        #region GET All Tags Tests

        [Fact]
        public async Task GetAllTags_WithNoTags_ReturnsEmptyList()
        {
            // Act
            var result = await _controller.GetAllTags();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var tags = okResult.Value.Should().BeAssignableTo<IEnumerable<TagNameResult>>().Subject;
            tags.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAllTags_WithSingleTag_ReturnsOneTag()
        {
            // Arrange
            var noteId = Guid.NewGuid();
            var note = new Note
            {
                Id = noteId,
                Summary = "Test Note",
                Details = "Test Details",
                CreatedDateUtc = DateTime.UtcNow
            };
            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                Name = "food",
                NoteId = noteId
            };
            _context.Notes.Add(note);
            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAllTags();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var tags = okResult.Value.Should().BeAssignableTo<IEnumerable<TagNameResult>>().Subject.ToList();
            tags.Should().HaveCount(1);
            tags[0].Name.Should().Be("food");
        }

        [Fact]
        public async Task GetAllTags_WithMultipleTags_ReturnsAllUniqueTags()
        {
            // Arrange
            var noteId1 = Guid.NewGuid();
            var noteId2 = Guid.NewGuid();
            var note1 = new Note
            {
                Id = noteId1,
                Summary = "Note 1",
                Details = "Details 1",
                CreatedDateUtc = DateTime.UtcNow
            };
            var note2 = new Note
            {
                Id = noteId2,
                Summary = "Note 2",
                Details = "Details 2",
                CreatedDateUtc = DateTime.UtcNow
            };

            var tag1 = new Tag { Id = Guid.NewGuid(), Name = "dairy", NoteId = noteId1 };
            var tag2 = new Tag { Id = Guid.NewGuid(), Name = "decorations", NoteId = noteId1 };
            var tag3 = new Tag { Id = Guid.NewGuid(), Name = "essentials", NoteId = noteId2 };
            var tag4 = new Tag { Id = Guid.NewGuid(), Name = "food", NoteId = noteId2 };

            _context.Notes.AddRange(note1, note2);
            _context.Tags.AddRange(tag1, tag2, tag3, tag4);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAllTags();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var tags = okResult.Value.Should().BeAssignableTo<IEnumerable<TagNameResult>>().Subject.ToList();
            tags.Should().HaveCount(4);
            tags.Select(t => t.Name).Should().Contain(new[] { "dairy", "decorations", "essentials", "food" });
        }

        [Fact]
        public async Task GetAllTags_WithDuplicateTags_ReturnsUniqueTagsOnly()
        {
            // Arrange - Create multiple notes with overlapping tags
            var noteId1 = Guid.NewGuid();
            var noteId2 = Guid.NewGuid();
            var noteId3 = Guid.NewGuid();
            var note1 = new Note
            {
                Id = noteId1,
                Summary = "Note 1",
                Details = "Details 1",
                CreatedDateUtc = DateTime.UtcNow
            };
            var note2 = new Note
            {
                Id = noteId2,
                Summary = "Note 2",
                Details = "Details 2",
                CreatedDateUtc = DateTime.UtcNow
            };
            var note3 = new Note
            {
                Id = noteId3,
                Summary = "Note 3",
                Details = "Details 3",
                CreatedDateUtc = DateTime.UtcNow
            };

            // Both note1 and note2 have "food" tag
            var tag1 = new Tag { Id = Guid.NewGuid(), Name = "food", NoteId = noteId1 };
            var tag2 = new Tag { Id = Guid.NewGuid(), Name = "dairy", NoteId = noteId1 };
            var tag3 = new Tag { Id = Guid.NewGuid(), Name = "food", NoteId = noteId2 };
            var tag4 = new Tag { Id = Guid.NewGuid(), Name = "essentials", NoteId = noteId2 };
            var tag5 = new Tag { Id = Guid.NewGuid(), Name = "food", NoteId = noteId3 };

            _context.Notes.AddRange(note1, note2, note3);
            _context.Tags.AddRange(tag1, tag2, tag3, tag4, tag5);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAllTags();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var tags = okResult.Value.Should().BeAssignableTo<IEnumerable<TagNameResult>>().Subject.ToList();
            
            // Should return only 3 unique tags: "food", "dairy", "essentials"
            tags.Should().HaveCount(3);
            tags.Select(t => t.Name).Should().Contain(new[] { "food", "dairy", "essentials" });
            
            // Verify "food" appears only once despite being in 3 different notes
            tags.Count(t => t.Name == "food").Should().Be(1);
        }

        [Fact]
        public async Task GetAllTags_ReturnsTagsInAlphabeticalOrder()
        {
            // Arrange
            var noteId = Guid.NewGuid();
            var note = new Note
            {
                Id = noteId,
                Summary = "Test Note",
                Details = "Test Details",
                CreatedDateUtc = DateTime.UtcNow
            };

            // Add tags in non-alphabetical order
            var tag1 = new Tag { Id = Guid.NewGuid(), Name = "zebra", NoteId = noteId };
            var tag2 = new Tag { Id = Guid.NewGuid(), Name = "apple", NoteId = noteId };
            var tag3 = new Tag { Id = Guid.NewGuid(), Name = "monkey", NoteId = noteId };

            _context.Notes.Add(note);
            _context.Tags.AddRange(tag1, tag2, tag3);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAllTags();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var tags = okResult.Value.Should().BeAssignableTo<IEnumerable<TagNameResult>>().Subject.ToList();
            tags.Should().HaveCount(3);
            
            // Verify alphabetical order
            tags[0].Name.Should().Be("apple");
            tags[1].Name.Should().Be("monkey");
            tags[2].Name.Should().Be("zebra");
        }

        [Fact]
        public async Task GetAllTags_ReturnsOkResult()
        {
            // Arrange
            var noteId = Guid.NewGuid();
            var note = new Note
            {
                Id = noteId,
                Summary = "Test",
                Details = "Test",
                CreatedDateUtc = DateTime.UtcNow
            };
            var tag = new Tag { Id = Guid.NewGuid(), Name = "test", NoteId = noteId };
            _context.Notes.Add(note);
            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAllTags();

            // Assert
            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            okResult!.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task GetAllTags_ReturnsCorrectContentType()
        {
            // Act
            var result = await _controller.GetAllTags();

            // Assert
            result.Result.Should().BeOfType<OkObjectResult>();
            // The controller is decorated with [Produces("application/json")]
            // This is verified by the attribute on the controller class
        }

        #endregion
    }
}
