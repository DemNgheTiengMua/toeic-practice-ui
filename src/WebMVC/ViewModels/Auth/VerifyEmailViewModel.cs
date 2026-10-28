using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

public sealed class VerifyEmailViewModel
{
    public string Token { get; set; } = "";

    /// <summary>null before the learner confirms; true or false after the API answers.</summary>
    public bool? Verified { get; set; }
}
