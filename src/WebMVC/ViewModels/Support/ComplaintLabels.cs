using Contracts;
namespace WebMVC.Models;
public static class ComplaintLabels
{
    public static string Type(ComplaintTypeDto type) => type switch
    {
        ComplaintTypeDto.MentorNoShow => "Mentor did not show up",
        ComplaintTypeDto.MentorQuality => "Mentor quality issue",
        ComplaintTypeDto.GradingIssue => "Grading issue",
        ComplaintTypeDto.TechnicalError => "Technical error",
        ComplaintTypeDto.PaymentIssue => "Payment issue",
        _ => type.ToString()
    };
}
