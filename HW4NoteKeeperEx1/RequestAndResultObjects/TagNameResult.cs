namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Represents a tag name returned by the GET /tags endpoint.
    /// </summary>
    public class TagNameResult
    {
        /// <summary>
        /// Gets or sets the name of the tag.
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}
