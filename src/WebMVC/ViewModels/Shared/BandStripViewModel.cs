namespace WebMVC.Models;

/// <summary>
/// #161: the band strip, described for rendering. The number of columns and their widths come from
/// <see cref="Bands"/>, never from the stylesheet: a certificate with four bands draws four, and a band
/// covering a quarter of the scale is a quarter of the width.
/// </summary>
/// <param name="Note">The sentence under the strip. It names the range, so a reader can check the scale
/// the columns were laid out against.</param>
public sealed record BandStripViewModel(IReadOnlyList<BandStripItemViewModel> Bands, string AriaLabel, string Note = "")
{
    /// <summary>One unit per score point the band covers, with a floor of one so a band that exists is
    /// never given zero width and its code always fits.</summary>
    public IReadOnlyList<int> Widths => Bands.Select(b => Math.Max(1, b.MaxTotal - b.MinTotal)).ToList();

    /// <summary>Emitted as a CSS custom property, so the browser's own grid arithmetic is the only thing
    /// that decides where a band boundary falls.</summary>
    public string ColumnTemplate => Widths.Count == 0 ? "none" : string.Join(" ", Widths.Select(w => $"{w}fr"));

    /// <summary>Builds a strip from a certificate's bands, ordered by score. Ordering is what makes the
    /// first tick really the lowest one, rather than whichever row the database happened to return first.</summary>
    public static BandStripViewModel From(
        IEnumerable<Contracts.BandDto> bands,
        string ownerName,
        Func<Contracts.BandDto, string> cssClass)
    {
        var ordered = bands.OrderBy(b => b.MinTotal).ToList();
        if (ordered.Count == 0)
        {
            return new BandStripViewModel([], $"{ownerName} has no score bands configured", "");
        }

        return new BandStripViewModel(
            ordered.Select(b => new BandStripItemViewModel(b.Code, b.MinTotal, b.MaxTotal, cssClass(b))).ToList(),
            $"{ownerName} score bands from {ordered[0].Code} at {ordered[0].MinTotal} "
                + $"to {ordered[^1].Code} at {ordered[^1].MaxTotal}",
            $"{ordered[0].MinTotal}–{ordered[^1].MaxTotal} scale. Thresholds come from this certificate's own band table.");
    }
}

public sealed record BandStripItemViewModel(string Label, int MinTotal, int MaxTotal, string CssClass);
