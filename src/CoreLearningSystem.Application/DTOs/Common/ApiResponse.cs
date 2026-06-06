using System.Collections.Generic;

namespace CoreLearningSystem.Application.DTOs.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string> Errors { get; set; } = new List<string>();

    public static ApiResponse<T> SuccessResponse(T data, string message = "Request processed successfully.")
    {
        return new ApiResponse<T> { Success = true, Message = message, Data = data };
    }

    public static ApiResponse<T> FailureResponse(List<string> errors, string message = "Request failed.")
    {
        return new ApiResponse<T> { Success = false, Message = message, Errors = errors };
    }

    public static ApiResponse<T> FailureResponse(string error, string message = "Request failed.")
    {
        return new ApiResponse<T> { Success = false, Message = message, Errors = new List<string> { error } };
    }
}
