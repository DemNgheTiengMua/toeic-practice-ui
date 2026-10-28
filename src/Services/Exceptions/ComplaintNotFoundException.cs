using System.Data;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;
using Services.Credits;

namespace Services;

public sealed class ComplaintNotFoundException(int complaintId)
    : ComplaintException(ApiError.Codes.ComplaintNotFound, $"Complaint {complaintId} was not found.", 404);
