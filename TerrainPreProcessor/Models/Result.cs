using System;

namespace TerrainPreProcessor.Models;

/// <summary>
/// 操作结果（无返回值）
/// </summary>
public readonly struct Result
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }
    
    /// <summary>
    /// 是否失败
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; }
    
    /// <summary>
    /// 异常对象（如果有）
    /// </summary>
    public Exception? Exception { get; }

    private Result(bool isSuccess, string errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result Success() => new(true, string.Empty, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static Result Failure(string message) => new(false, message, null);

    /// <summary>
    /// 创建失败结果（从异常）
    /// </summary>
    public static Result Failure(Exception ex) => new(false, ex.Message, ex);

    /// <summary>
    /// 如果失败则抛出异常
    /// </summary>
    public void ThrowIfFailure()
    {
        if (IsFailure)
            throw Exception ?? new InvalidOperationException(ErrorMessage);
    }

    /// <summary>
    /// 隐式转换为 bool
    /// </summary>
    public static implicit operator bool(Result result) => result.IsSuccess;
}

/// <summary>
/// 操作结果（带返回值）
/// </summary>
/// <typeparam name="T">返回值类型</typeparam>
public readonly struct Result<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }
    
    /// <summary>
    /// 是否失败
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// 返回值
    /// </summary>
    public T? Value { get; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; }
    
    /// <summary>
    /// 异常对象（如果有）
    /// </summary>
    public Exception? Exception { get; }

    private Result(bool isSuccess, T? value, string errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result<T> Success(T value) => new(true, value, string.Empty, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static Result<T> Failure(string message) => new(false, default, message, null);

    /// <summary>
    /// 创建失败结果（从异常）
    /// </summary>
    public static Result<T> Failure(Exception ex) => new(false, default, ex.Message, ex);

    /// <summary>
    /// 获取返回值或抛出异常
    /// </summary>
    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw Exception ?? new InvalidOperationException(ErrorMessage);
        return Value!;
    }

    /// <summary>
    /// 映射到新类型
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (IsFailure)
            return Result<TNew>.Failure(ErrorMessage);
        return Result<TNew>.Success(mapper(Value!));
    }

    /// <summary>
    /// 隐式转换为 bool
    /// </summary>
    public static implicit operator bool(Result<T> result) => result.IsSuccess;
}
