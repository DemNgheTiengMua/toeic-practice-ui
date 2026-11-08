using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #220 / D19: the authoring vocabulary - the words a staff member reads while describing a certificate
/// or a paper. One family per concept, and every family delegates its registration to
/// <see cref="DisplayLabels"/>, so a new member here reaches a test rather than a page.
/// </summary>
internal static class CatalogLabels
{
    public static void AddTo(IDictionary<Type, LabelFamily> families)
    {
        LabelFamily.Add<ExamStatusDto>(families, "Status not reported", [
            ("draft", "Draft"),
            ("published", "Published"),
            ("hidden", "Hidden"),
        ]);

        LabelFamily.Add<ExamPurposeDto>(families, "Purpose not reported", [
            ("mock", "Mock exam"),
            ("placement", "Placement test"),
            ("speaking_practice", "Speaking practice"),
            ("writing_practice", "Writing practice"),
        ]);

        LabelFamily.Add<DeliveryModeDto>(families, "Delivery not reported", [
            ("strict_exam", "Strict exam"),
            ("practice", "Practice session"),
        ]);

        LabelFamily.Add<LayoutTypeDto>(families, "Layout not reported", [
            ("STANDARD_VERTICAL", "Standard vertical"),
            ("SPLIT_HORIZONTAL", "Split horizontal"),
            ("MEDIA_CARD", "Media card"),
            ("CLUSTER_3", "Three-item cluster"),
        ]);

        LabelFamily.Add<NavigationModeDto>(families, "Navigation not reported", [
            ("FULL_SANDBOX", "Free navigation"),
            ("UNIT_LOCKED_SANDBOX", "One unit at a time"),
            ("AUDIO_DRIVEN", "Audio paced"),
            ("SYSTEM_PACED_AUTO", "System paced"),
            ("HYBRID_GROUP_LOCKED", "Group locked"),
        ]);

        LabelFamily.Add<QuestionTypeDto>(families, "Question type not reported", [
            ("mcq", "Multiple choice"),
            ("mcq_3", "Multiple choice, three options"),
            ("multi_select", "Multiple select"),
            ("inline_completion", "Inline completion"),
            ("dropdown", "Dropdown"),
            ("drag_drop", "Drag and drop"),
            ("free_response", "Free response"),
        ]);

        LabelFamily.Add<ReferenceKindDto>(families, "Reference not reported", [
            ("script", "Script"),
            ("image_desc", "Image description"),
        ]);

        LabelFamily.Add<ScoreAggregationRuleDto>(families, "Rule not reported", [
            ("sum", "Sum"),
            ("avg_half_step", "Average, half-point steps"),
            ("avg_whole", "Average, whole points"),
        ]);

        LabelFamily.Add<CardTypeDto>(families, "Card type not reported", [
            ("rule", "Rule"),
            ("pitfall", "Pitfall"),
            ("example", "Example"),
            ("micro_check", "Micro check"),
        ]);

        LabelFamily.Add<MediaArea>(families, "Area not reported", [
            ("Imports", "Imports"),
            ("Exams", "Exams"),
        ]);

        LabelFamily.Add<PathStepTypeDto>(families, "Step type not reported", [
            ("diagnostic_drill", "Diagnostic drill"),
            ("focus_practice", "Focused practice"),
            ("review_spaced", "Spaced review"),
            ("concept_lesson", "Concept lesson"),
            ("expanded_remedial", "Remedial practice"),
            ("mock_section", "Mock section"),
        ]);

        LabelFamily.Add<SizingTierDto>(families, "Size not reported", [
            ("mastered", "Mastered"),
            ("developing", "Developing"),
            ("low", "Needs work"),
        ]);

        LabelFamily.Add<StepRouteDto>(families, "Route not reported", [
            ("item_selector", "Question drill"),
            ("lesson_reader", "Lesson"),
            ("attempt_engine", "Timed sitting"),
        ]);
    }
}
