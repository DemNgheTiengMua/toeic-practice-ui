using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class ScoreConversionException(string code, string message) : Exception(message)
{
    public const string InvalidData = "score_conversion_invalid";
    public const string CertificateNotFound = "certificate_not_found";
    public const string NonMonotonicScales = "non_monotonic_scales";
    public const string OverlappingBands = "overlapping_bands";
    public const string InvalidBandRange = "invalid_band_range";
    public const string SkillScaleNotConfigured = "skill_scale_not_configured";

    public string Code { get; } = code;
}
