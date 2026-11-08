using System;
using System.Collections.Generic;
using System.Linq;

namespace Contracts;

/// <summary>
/// #226 / ADM-12: one of the six publish invariants, as the rail shows it.
/// <para>
/// <b>Six rows, never one.</b> A gate that collapses its checks into "cannot publish" sends the author
/// hunting; ADM-13 wants each unmet invariant named with a route to the exact deficit. This type is
/// that row: the invariant's own code, whether it held, how many deficits it found, and the element the
/// rail's "jump to issue" should focus.
/// </para>
/// </summary>
public sealed record ExamInvariantDto(
    string Code,
    string Title,
    bool Satisfied,
    int DeficitCount,
    string? FirstElementTarget);
