 
 
using HW4NoteKeeperEx1.Models;
using Microsoft.EntityFrameworkCore;

namespace HW4NoteKeeperEx1.Data
{
    /// <summary>
    /// Coordinates Entity Framework functionality for a given data model is the database context class
    /// </summary>
    /// <seealso cref="Microsoft.EntityFrameworkCore.DbContext" />
    /// <remarks>Step 6</remarks>
    public class MyDatabaseContext : DbContext
    {
         

        /// <summary>
        /// Initializes a new instance of the <see cref="MyDatabaseContext"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <remarks>Step 6a</remarks>
        public MyDatabaseContext(DbContextOptions<MyDatabaseContext> options) : base(options)
        { }

        /// <summary>
        /// Represents the Note table (Entity Set)
        /// </summary>
        /// <value>
        /// The notes.
        /// </value>
        /// <remarks>Step 6b</remarks>
        public DbSet<Note> Notes { get; set; }

        /// <summary>
        /// Represents the Tag table (Entity Set)
        /// </summary>
        /// <value>
        /// The Tags.
        /// </value>
        public DbSet<Tag> Tags { get; set; }

        /// <summary>
        /// Override this method to further configure the model that was discovered by convention from the entity types
        /// exposed in <see cref="T:Microsoft.EntityFrameworkCore.DbSet`1" /> properties on your derived context. 
        /// The resulting model may be cached
        /// and re-used for subsequent instances of your derived context.
        /// </summary>
        /// <param name="modelBuilder">The builder being used to construct the model for this context. 
        /// Databases (and other extensions) typically
        /// define extension methods on this object that allow you to configure aspects of the model that are specific
        /// to a given database.</param>
        /// <remarks>
        /// Step 6c
        /// If a model is explicitly set on the options for this context 
        /// (via <see cref="M:Microsoft.EntityFrameworkCore.DbContextOptionsBuilder.UseModel(Microsoft.EntityFrameworkCore.Metadata.IModel)" />)
        /// then this method will not be run.
        /// </remarks>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //// Define the Address table with a name of Address
            //// By default it would be plural.
            modelBuilder.Entity<Tag>().ToTable("Tag");

            //// Adds the Note to the entity model linking it to the Customer table
            modelBuilder.Entity<Note>().ToTable("Note")
                .HasMany(c => c.Tags)
                .WithOne(t => t.Note)
                .HasForeignKey(t => t.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        /// <summary>
        /// Configure enhanced logging
        /// </summary>
        /// <param name="optionsBuilder">The operation builder</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        { 
            //optionsBuilder.EnableDetailedErrors();
        }
    }


}

