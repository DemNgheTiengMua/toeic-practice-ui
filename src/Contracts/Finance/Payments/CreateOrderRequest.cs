using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record CreateOrderRequest(
    [property: Range(1, 1000, ErrorMessage = "Credits must be between 1 and 1,000.")]
    int Credits);
