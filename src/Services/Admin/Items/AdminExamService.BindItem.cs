using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>
    /// #188: the second act of authoring. An item is created with no exam reference - that is what
    /// #184 made true - so putting it on a paper is a separate, explicit binding rather than a
    /// property of the item.
    /// <para>
    /// Why here and not in <see cref="AdminItemService"/>: a binding is a row of <c>ExamItems</c>,
    /// which carries the exam, its certificate and its section, and is subject to #157's structure
    /// lock and INV-6. Those are the exam's rules, so the exam service owns the write. The pool
    /// service stays free of any exam concept, which is the property #231 needs to delete the legacy
    /// tables without unpicking this one.
    /// </para>
    /// <para>
    /// The position is the paper's, not the item's: <c>UQ_ExamItems_Exam_Section_Order</c> makes one
    /// position per section unique, so binding is also the act that decides where on the paper the
    /// version sits.
    /// </para>
    /// </summary>
    public async Task<ExamItemBindingDto> BindItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var section = await db.Sections
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Section {request.SectionId} does not belong to certificate {exam.CertId}, so it cannot hold an item of this paper.");

        var version = await db.ItemVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ItemVersionId == request.ItemVersionId, cancellationToken)
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Item version {request.ItemVersionId} not found.");

        var item = await db.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ItemId == version.ItemId, cancellationToken)
            ?? throw new ItemNotFoundException(version.ItemId);

        // The composite key would refuse this pair too; the sentence is why it is checked here.
        if (!string.Equals(item.Modality, section.Modality, StringComparison.Ordinal))
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Section '{section.Code}' is {section.Modality}; item version {version.ItemVersionId} is {item.Modality}. A section can only bind an item of its own skill.");
        }

        // #157: a binding changes which items a finished attempt was scored over.
        await RequireStructureEditableAsync(examId, cancellationToken);

        var binding = new ExamItem
        {
            ExamId = exam.ExamId,
            SectionId = section.SectionId,
            CertId = exam.CertId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = section.Modality,
            DisplayOrder = request.DisplayOrder,
            UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1,
        };
        db.ExamItems.Add(binding);
        exam.UpdatedAt = clock.GetUtcNow();

        try
        {
            // One save: the binding and the paper's timestamp land together, so a refusal leaves
            // neither behind.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql)
        {
            // The two database refusals a caller can actually provoke, named rather than left as a
            // 500: a duplicate position, and INV-6 (a production paper drawing demo stock).
            throw new AdminExamException(ApiError.Codes.Validation, ExplainBindingRefusal(sql), 400);
        }

        return new ExamItemBindingDto(
            binding.ExamItemId,
            binding.ExamId,
            binding.SectionId,
            binding.CertId,
            binding.ItemId,
            binding.ItemVersionId,
            EnumCodes.Parse<ModalityDto>(binding.Modality),
            binding.DisplayOrder,
            binding.UnitNumber);
    }

    private static string ExplainBindingRefusal(SqlException error) => error.Number switch
    {
        // 50049: trg_ExamItems_ProductionBindsProduction
        50049 => "INV-6: a production exam cannot bind a demo-tier item. Promote the item through review, or make the paper demo.",
        // 2627/2601: a unique index, and the only unique index a binding can collide with.
        2627 or 2601 => "That position in this section is already taken. A paper has one item per position per section.",
        _ => error.Message,
    };
}
