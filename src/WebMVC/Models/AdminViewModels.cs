using Contracts;

namespace WebMVC.Models;

public sealed record AdminOrdersViewModel(
    AdminOrdersSummaryDto Summary,
    string? Query,
    string? Status,
    string? Gateway,
    int CurrentPage,
    AdminOrderDetailDto? SelectedOrder = null);

public sealed record AdminUsersViewModel(
    AdminUsersSummaryDto Summary,
    string? Query,
    string? Status,
    int CurrentPage,
    AdminUserDto? AdjustUser = null,
    string? FormError = null);
