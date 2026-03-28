namespace HW4NoteKeeperEx1.CustomSettings
{
    /// <summary>
    /// Application insights settings
    /// </summary>
    public class ApplicationInsights
    {
        /// <summary>
        /// The API Authentication key for quick pulse telemetry
        /// </summary>
        public string? AuthenticationApiKey { get; set; }

        /// <summary>
        /// Adaptive sampling reduces the amount of logging but not all entries are logged, this reduces expense
        /// </summary>
        /// <value>True is enabled and some entries will be missed, false is not enabled all items are logged increasing expense</value>
        public bool EnableAdaptiveSampling { get; set; } = true;

        /// <summary>
        /// Enables the snapshot collector in the SDK if true
        /// </summary>
        public bool EnableSnapshotCollectorInSdk { get; set; } = true;

        /// <summary>
        /// If true indicates development mode is enabled
        /// </summary>
        /// <remarks>
        /// Development Mode
        /// Purpose: When DevelopmentMode is set to true, it indicates that the application is running in a development environment.
        ///  Behavior:
        ///     - Verbose Logging: More detailed logs and telemetry data are collected.This helps developers diagnose issues and understand application behavior during development.
        ///     - Performance Impact: Since more data is collected, there might be a slight performance overhead.However, this is acceptable in a development environment where the primary goal is debugging and testing.
        ///     - Cost: Increased data collection can lead to higher costs if telemetry data is sent to Application Insights. However, in development, this is usually not a concern as the focus is on thorough testing.
        ///
        /// Production Mode
        /// Purpose: When DevelopmentMode is set to false, it indicates that the application is running in a production environment.
        ///    Behavior:
        ///    - Optimized Logging: Telemetry data collection is optimized to reduce overhead and costs. Only essential data is collected to monitor the application's health and performance.
        ///    - Performance: The application runs with minimal performance impact from telemetry collection, ensuring a smooth user experience.
        ///    - Cost: Reduced data collection helps in managing costs associated with Application Insights, as only critical data is logged.
        /// </remarks>
        public bool DevelopmentMode { get; set; } = true;
         
    }
}
