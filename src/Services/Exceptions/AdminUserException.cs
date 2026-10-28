using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public class AdminUserException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}
