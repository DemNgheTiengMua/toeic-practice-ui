using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class ExamQuickIngestService
{

    public async Task<string> GetSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default)
    {
        if (db == null) throw new InvalidOperationException("CompanionDbContext is required to generate sample template.");

        var section = await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.SectionId == sectionId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException("SectionNotFound", $"Section with ID {sectionId} was not found.", 404);
        }

        return GenerateSectionSampleTemplate(section);
    }

    public static string GenerateSectionSampleTemplate(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var allowed = section.AllowedComponentTypesJson ?? string.Empty;

        // 1. Keyword Free-Response Mode (e.g. TOEIC Writing Q1-5 photo sentences)
        if (section.RequiredKeywordCount > 0)
        {
            return """
                PROMPT: Write a sentence based on the photograph using the two keywords provided.
                KEYWORDS: photograph, yesterday
                MIN_WORDS: 5
                MAX_WORDS: 30

                PROMPT: Write a sentence based on the photograph using the two keywords provided.
                KEYWORDS: airport, luggage
                MIN_WORDS: 5
                MAX_WORDS: 30
                """;
        }

        // 2. Free-Response Essay / Speaking Mode (e.g. IELTS Writing Task 2, Speaking Part 2)
        if (string.Equals(section.Modality, "writing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section.Modality, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PROMPT: Some people think universities should focus on practical job skills, while others argue academic theory is more important. Discuss both views and give your opinion.
                MIN_WORDS: 250
                PREP: 0
                RESPONSE: 2400
                """;
        }

        // 3. Inline Completion Mode
        if (allowed.Contains("inline_completion", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PASSAGE: The university botanical garden is open daily from 9:00 AM to 5:00 PM. Admission is free for university students and staff members with valid identification. Guided tours are conducted every Saturday at 11:00 AM starting from the visitor center.

                1. What time does the botanical garden open each day?
                ANSWER: 9:00 AM | 9 AM | 9am
                MAX_WORDS: 2

                2. What is required for students to receive free admission?
                ANSWER: valid identification | valid ID | ID
                MAX_WORDS: 2
                """;
        }

        // 4. Multi-Select Mode
        if (allowed.Contains("multi_select", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PASSAGE: All laboratory researchers must submit an annual safety audit and equipment inventory before October 15. In addition, chemical spill emergency kits must be inspected monthly by the lab coordinator.

                1. Which documents must researchers submit before October 15?
                *A. Safety audit
                B. Financial budget
                *C. Equipment inventory
                D. Travel reimbursement
                ANSWER: A, C
                MAX_SELECTIONS: 2
                """;
        }

        // 5. Audio-Only 3-Option Mode (e.g. TOEIC Part 2 Q-and-R)
        if (section.OptionCount == 3 || section.HideOptionText)
        {
            return """
                1. A
                TRANSCRIPT: Where is the nearest bus stop?
                2. C
                TRANSCRIPT: When does the flight to Tokyo depart?
                3. B
                TRANSCRIPT: Who is leading the safety orientation?
                """;
        }

        // 6. Clustered Reading Comprehension Mode
        if (section.RequirePassage || section.ClusterSize > 1)
        {
            return """
                PASSAGE: GreenLeaf Logistics has announced an expansion of its refrigerated courier services to meet rising demand from organic food producers. The fleet will add 25 electric delivery vans by the end of Q3.

                1. What is the main purpose of the announcement?
                *A. To announce service expansion
                B. To advertise job openings
                C. To explain rate increases
                D. To report financial earnings
                EXPLANATION: The text states GreenLeaf announced an expansion of refrigerated courier services.

                2. How many new vehicles will be added?
                A. 10
                B. 15
                *C. 25
                D. 50
                EXPLANATION: The fleet will add 25 electric delivery vans.
                """;
        }

        // 7. Standard MCQ Mode (4 options)
        return """
            1. Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.
            *A. next
            B. nearly
            C. soon
            D. highly
            EXPLANATION: The temporal adjective 'next' correctly precedes a day of the week to denote future timing.
            CATEGORY: grammar_prep

            Question 2: All visitors must present photo identification upon _____ the research building.
            (A) enter
            *(B) entering
            (C) entered
            (D) entrance
            ANSWER: B
            EXPLANATION: The preposition 'upon' must be followed by a gerund ('entering').
            CATEGORY: grammar_gerund
            """;
    }
}
