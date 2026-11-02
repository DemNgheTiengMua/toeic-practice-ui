
namespace BusinessObjects;

/// <summary>One certificate a learner can work on, as the switcher lists it.</summary>
/// <param name="SectionNoun">#161: the word this certificate puts before a section code — "Part" on a
/// TOEIC paper, "Section" by default. A label the views read instead of switching on
/// <paramref name="Code"/>, so a certificate the code has never heard of still reads correctly.</param>
public sealed record CertificateOption(string Code, string Name, string SectionNoun = "Section");
