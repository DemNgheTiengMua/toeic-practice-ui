using BusinessObjects;

namespace Services;

public sealed class EmailNotVerifiedException(string message = "Email verification is required before purchasing credits.")
    : Exception(message);
