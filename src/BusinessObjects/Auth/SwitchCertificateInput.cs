using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>The certificate a learner is switching to, named by its catalogue code.</summary>
public sealed record SwitchCertificateInput(
    [property: Required(ErrorMessage = "Choose a certificate.")]
    string Code);
