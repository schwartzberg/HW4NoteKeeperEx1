using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HW4NoteKeeperEx1.Models
{
    /// <summary>
    /// Represents a tag entity associated with a note in the database.
    /// </summary>
    [Table("Tag")]
    public class Tag
    {
        /// <summary>
        /// Gets or sets the unique identifier for the tag.
        /// </summary>
        [Key]
        [Column("Id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the note this tag is associated with.
        /// Foreign key to the Note table.
        /// </summary>
        [Required]
        [Column("NoteId")]
        public Guid NoteId { get; set; }

        /// <summary>
        /// Gets or sets the name of the tag (1-30 characters).
        /// If AI generates a tag longer than 30 characters, it will be truncated.
        /// </summary>
        [Required]
        [StringLength(30, MinimumLength = 1)]
        [Column("Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Navigation property back to the Note this tag belongs to.
        /// </summary>
        [ForeignKey("NoteId")]
        public Note Note { get; set; } = null!;
    }
}
