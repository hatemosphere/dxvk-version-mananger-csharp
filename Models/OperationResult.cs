namespace DxvkVersionManager.Models;

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Warning { get; set; }
    public string? Details { get; set; }
    
    public static OperationResult Successful(string message)
    {
        return new OperationResult { Success = true, Message = message };
    }
    
    public static OperationResult Successful(string message, string details)
    {
        return new OperationResult { Success = true, Message = message, Details = details };
    }
    
    public static OperationResult Failed(string message)
    {
        return new OperationResult { Success = false, Message = message };
    }
    
    public static OperationResult Failed(string message, string details)
    {
        return new OperationResult { Success = false, Message = message, Details = details };
    }
}