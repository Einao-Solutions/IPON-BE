using patentdesign.Models;

namespace patentdesign.Dtos.Response
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }

        public ApiResponse() { }

        public ApiResponse(bool success, string? message = null, T? data = default)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public static ApiResponse<T> Ok(T data, string? message = null) => new(true, message, data);
        public static ApiResponse<T> Fail(string message) => new(false, message, default);
    }
    public class RestorationDto
    {
        public string? FileNumber { get; set; }
        public ApplicationStatuses? FileStatus { get; set; }
        public string? Applicant { get; set; }
        public string? PaymentId { get; set; }
        public string? ApplicationId { get; set; }
        public string? Cost { get; set; }

    }
}
