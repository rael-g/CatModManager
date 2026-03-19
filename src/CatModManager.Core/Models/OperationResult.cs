using System;

namespace CatModManager.Core.Models;

public class OperationResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    protected OperationResult(bool success, string? errorMessage = null, Exception? ex = null)
    {
        IsSuccess = success;
        ErrorMessage = errorMessage;
        Exception = ex;
    }

    public static OperationResult Success() => new(true);
    public static OperationResult Failure(string message, Exception? ex = null) => new(false, message, ex);
}

public class OperationResult<T> : OperationResult
{
    public T? Value { get; }

    private OperationResult(bool success, T? value, string? errorMessage = null, Exception? ex = null) 
        : base(success, errorMessage, ex)
    {
        Value = value;
    }

    public static OperationResult<T> Success(T value) => new(true, value);
    public static new OperationResult<T> Failure(string message, Exception? ex = null) => new(false, default, message, ex);
}
