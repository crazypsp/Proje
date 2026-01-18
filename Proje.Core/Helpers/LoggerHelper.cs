using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Core.Helpers
{
    public static class LoggerHelper
    {
        private static ILogger _logger;

        static LoggerHelper()
        {
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .WriteTo.Console()
                .CreateLogger();
        }

        public static void LogInformation(string message)
        {
            _logger.Information(message);
        }

        public static void LogError(Exception ex, string message)
        {
            _logger.Error(ex, message);
        }

        public static void LogWarning(string message)
        {
            _logger.Warning(message);
        }
    }
}
