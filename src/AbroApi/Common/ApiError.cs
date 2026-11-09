namespace AbroApi.Common;

/// Error shape from docs/api-contract.md: { code, message, fields? }.
public record ApiError(string Code, string Message, Dictionary<string, string>? Fields = null)
{
    public static IResult Result(int status, string code, string message) =>
        Results.Json(new ApiError(code, message), statusCode: status);

    public static IResult Validation(string field, string message) =>
        Results.Json(
            new ApiError("validation_failed", message, new() { [field] = message }),
            statusCode: 400);
}
