using Microsoft.Extensions.Options;
using patentdesign.Models;
using patentdesign.Services.Interface;

namespace patentdesign.Services.Implementation
{
    public class LoggerService : ILoggerService
        {
            private static object mutex;
        private string logPath;

            public LoggerService(IOptions<PatentDesignDBSettings> patentDesignDbSettings)
            {
                //  _config = config;
                mutex = new object();
            logPath = patentDesignDbSettings.Value.LogPath;
            }

            public void Log(string message)
            {
                try
                {
                string directory = @"C:\IpoApiLog";
                //string directory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Logs"));
                //string directory = $"{Directory.GetCurrentDirectory()}/Logs";
                if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    string filepath = directory + @"\" + DateTime.Now.Date.ToString("dd-MMM-yyyy") + ".txt";
                    lock (mutex)
                    {
                        File.AppendAllText(filepath, "Event Time: " + DateTime.Now.ToString() + " | Message: " + message + Environment.NewLine);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not log info to file: {ex.Message}");
                }
            }

            public void LogError(Exception exception, string message)
            {
                try
                {
                //string directory = @"C:\IpoApiLog";
                string directory = logPath;
                //string directory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Logs"));
                    //string directory = $"{Directory.GetCurrentDirectory()}/Logs";
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    string filepath = directory + @"\" + DateTime.Now.Date.ToString("dd-MMM-yyyy") + ".txt";
                    lock (mutex)
                    {
                        File.AppendAllText(filepath, "Event Time: " + DateTime.Now.ToString() + " | Message: " + message + " | Exception: " + exception + Environment.NewLine);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not log info to file: {ex.Message}");
                }
            }
        }
    
}
