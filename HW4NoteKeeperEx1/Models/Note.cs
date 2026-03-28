using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HW4NoteKeeperEx1.Models
{
    /// <summary>
    /// Represents a note entity in the database with summary, details, timestamps, and associated tags.
    /// </summary>
    [Table("note")]
    public class Note
    {
        /// <summary>
        /// Gets or sets the unique identifier for the note.
        /// </summary>
        [Key]
        [Column("Id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the brief summary of the note (1-60 characters).
        /// </summary>
        [Required]
        [StringLength(60, MinimumLength = 1)]
        [Column("summary")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the detailed content of the note (1-1024 characters).
        /// </summary>
        [Required]
        [StringLength(1024, MinimumLength = 1)]
        [Column("details")]
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the UTC timestamp when the note was created.
        /// </summary>
        [Required]
        [Column("CreatedDateUtc")]
        public DateTimeOffset CreatedDateUtc { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when the note was last modified.
        /// Null if the note has never been modified.
        /// </summary>
        [Column("ModifiedDateUtc")]
        public DateTimeOffset? ModifiedDateUtc { get; set; }

        /// <summary>
        /// Gets or sets the collection of tags associated with this note.
        /// Navigation property for the one-to-many relationship.
        /// </summary>
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}
