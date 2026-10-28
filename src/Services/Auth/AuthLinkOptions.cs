using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>Where emailed links point: the MVC site, the only thing a learner's browser talks to.</summary>
public sealed class AuthLinkOptions
{
    public const string SectionName = "AuthLinks";

    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
}
