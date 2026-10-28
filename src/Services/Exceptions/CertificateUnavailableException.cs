using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

using Services.Credits;

namespace Services;

/// <summary>Thrown when a learner asks to switch to a certificate that doesn't exist or is retired.</summary>
public sealed class CertificateUnavailableException(string code)
    : Exception($"Certificate '{code}' is not available.")
{
    public string CertificateCode { get; } = code;
}
