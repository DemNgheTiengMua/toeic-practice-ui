
namespace Contracts;

/// <summary>Wire DTOs for the learner's own context and dashboard (issue #7).</summary>
/// <param name="SectionNoun">#161: the certificate's own word for a section ("Part", "Section"), so a
/// page can label "Part 3" without knowing which certificate it is rendering.</param>
public sealed record CertificateOptionDto(string Code, string Name, string SectionNoun = "Section");
