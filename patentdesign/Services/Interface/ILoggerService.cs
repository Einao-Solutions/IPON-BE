namespace patentdesign.Services.Interface
{
    public interface ILoggerService
    {
        void Log(string message);
        void LogError(Exception ex, string message);
    }
}
