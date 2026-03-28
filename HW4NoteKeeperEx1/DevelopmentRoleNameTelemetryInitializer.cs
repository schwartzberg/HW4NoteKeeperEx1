using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace HW4NoteKeeperEx1
{
    /// <summary>
    /// Initializes the telemetry with the development role name.
    /// </summary>
    public class DevelopmentRoleNameTelemetryInitializer : ITelemetryInitializer
    {
        private readonly IWebHostEnvironment _env;

        /// <summary>
        /// Initializes a new instance of the <see cref="DevelopmentRoleNameTelemetryInitializer"/> class.
        /// </summary>
        /// <param name="env">The web host environment.</param>
        public DevelopmentRoleNameTelemetryInitializer(IWebHostEnvironment env)
        {
            _env = env;
        }

        /// <summary>
        /// Initializes the telemetry.
        /// </summary>
        /// <param name="telemetry">The telemetry to initialize.</param>
        public void Initialize(ITelemetry telemetry)
        {
            if (_env.IsDevelopment())
            {
                // Set the role name to the machine name if running in development environment
                telemetry.Context.Cloud.RoleName = Environment.MachineName;
            }
        }
    }

}
