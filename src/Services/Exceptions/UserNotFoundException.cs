using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed class UserNotFoundException(int userId)
    : AdminUserException(ApiError.Codes.UserNotFound, $"User with id {userId} not found.", 404);
