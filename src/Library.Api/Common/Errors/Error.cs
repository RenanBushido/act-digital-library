namespace Library.Api.Common.Errors;

public sealed record Error(string Type, int StatusCode, string Title);
