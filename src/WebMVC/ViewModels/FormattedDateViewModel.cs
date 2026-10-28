namespace WebMVC.Models;

public sealed record FormattedDateViewModel(string Date, string Full, string Session, string Time)
{
    public static IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Prepare(
        IEnumerable<DateTimeOffset> instants, DateTimeFormatter formatter) => instants.Distinct().ToDictionary(
            instant => instant, instant => new FormattedDateViewModel(formatter.FormatDate(instant), formatter.Format(instant),
                formatter.FormatCustom(instant, "dddd, MMM d — h:mm tt"), formatter.FormatCustom(instant, "HH:mm")));
}
