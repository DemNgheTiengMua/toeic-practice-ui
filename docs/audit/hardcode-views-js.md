# Hardcode audit — views & client JS

Generated: 2026-10-31
Total rows: 84 · BLOCKER 5 · WRONG_DISPLAY 30 · COSMETIC 49

| path:line | CATEGORY | what it assumes | breaks when | severity |
|---|---|---|---|---|
| src/WebMVC/Views/Home/Index.cshtml:5 | CERT_NAME | Meta description enumerates exactly "TOEIC, IELTS and VSTEP" | a fourth certificate is added or one renamed/removed | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:66 | CERT_NAME | Hero chip links to `/certificates#cert-toeic` with label TOEIC | certificate code is not TOEIC (anchor 404s) | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:67 | CERT_NAME | Hero chip links to `/certificates#cert-ielts` | certificate code is not IELTS | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:68 | CERT_NAME | Hero chip links to `/certificates#cert-vstep` | certificate code is not VSTEP | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:75 | CERT_NAME | Preview panel eyebrow hardcoded "TOEIC · SAMPLE" | active certificate is anything else | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:85 | SCORE_SCALE | Sample dashboard "TARGET" shows literal 750 (TOEIC 10–990 midpoint) | any certificate not scored on 10–990 | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:86 | CERT_NAME | "TOEIC" caption under the 750 target | non-TOEIC certificate | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:101 | SECTION_CODE | Preview row "Listening practice — Part 3" assumes TOEIC part numbering | certificate without P1..P7 parts | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:122 | CERT_NAME | Landing card "01 / TOEIC" | certificate set changes | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:123 | SKILL_CODE | TOEIC card subtitle hardcodes "Listening · Reading" | TOEIC gains/loses a skill | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:127 | CERT_NAME | Landing card "02 / IELTS" | certificate set changes | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:128 | SKILL_CODE | IELTS card subtitle hardcodes all four skills | cert skill set differs | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:132 | CERT_NAME | Landing card "03 / VSTEP" | certificate set changes | COSMETIC |
| src/WebMVC/Views/Home/Index.cshtml:133 | SKILL_CODE | VSTEP card subtitle "All four skills" | cert has 2 or 3 skills | COSMETIC |
| src/WebMVC/Views/Certificates/Index.cshtml:4 | CERT_NAME | Description names only TOEIC, IELTS, VSTEP | catalogue grows | COSMETIC |
| src/WebMVC/Views/Certificates/Index.cshtml:15 | CERT_NAME | H1 "Three certificates, one account" — count baked into copy | catalogue has ≠3 certificates | WRONG_DISPLAY |
| src/WebMVC/Views/Certificates/Index.cshtml:77 | BAND_CODE | aria-label takes `Bands.First()`/`Bands.Last()` as lowest/highest and prints raw `Code` | bands arrive unordered, or codes are not CEFR-style | WRONG_DISPLAY |
| src/WebMVC/Views/Certificates/Index.cshtml:89 | BAND_CODE | Note "Bands follow the certificate's standard level mapping" presumes a fixed official mapping | admin edits thresholds in the score-conversion editor | COSMETIC |
| src/WebMVC/Views/WeaknessAnalysis/Index.cshtml:78 | CERT_NAME | `Certificate.Code == "TOEIC" ? "Part " : "Section "` switch on cert code | a new cert uses parts, or TOEIC is renamed | COSMETIC |
| src/WebMVC/Views/WeaknessAnalysis/Index.cshtml:114 | CERT_NAME | Same TOEIC string switch in priority-practice sentence | new cert with part-style sections | COSMETIC |
| src/WebMVC/Views/WeaknessAnalysis/Index.cshtml:117 | CERT_NAME | Same TOEIC switch in button label | new cert with part-style sections | COSMETIC |
| src/WebMVC/Views/WeaknessAnalysis/Index.cshtml:118 | CERT_NAME | Same TOEIC switch in saved-questions link text | new cert with part-style sections | COSMETIC |
| src/WebMVC/ViewModels/Dashboard/DashboardWeaknessViewModel.cs:9 | CERT_NAME | `CertificateCode == "TOEIC" ? "PART" : "SECTION"` eyebrow | any certificate whose sections are parts but is not TOEIC | COSMETIC |
| src/WebMVC/Views/Practice/Index.cshtml:5 | SKILL_CODE | Meta description "listening and reading sections" | certificate whose practice bank is other skills | COSMETIC |
| src/WebMVC/Views/Practice/Index.cshtml:13 | SKILL_CODE | Lead copy splits the world into "Listening and reading … Speaking and writing" | certificate has a different skill set | WRONG_DISPLAY |
| src/WebMVC/Views/Practice/Index.cshtml:42 | SKILL_CODE | Empty state keyed on `ListeningSections.Count == 0 && ReadingSections.Count == 0` | a certificate with only speaking/writing sections shows "No questions to practice yet" | BLOCKER |
| src/WebMVC/Views/Practice/Index.cshtml:60 | SKILL_CODE | Presence of `ListeningSections` drives the whole speaking/writing exclusion branch | cert with no listening sections skips the branch entirely | WRONG_DISPLAY |
| src/WebMVC/Views/Practice/Index.cshtml:101 | SKILL_CODE | Section heading literal "Listening" | skill named "Listening Comprehension" or a 5th skill | COSMETIC |
| src/WebMVC/Views/Practice/Index.cshtml:129 | SKILL_CODE | Section heading literal "Reading" | skill renamed or cert has no reading | COSMETIC |
| src/WebMVC/Views/Practice/Index.cshtml:177 | SKILL_CODE | Copy "Free practice drills are available for listening and reading (MCQ) sections" | cert with no listening/reading | COSMETIC |
| src/WebMVC/ViewModels/Practice/PracticeCatalogViewModel.cs:7 | SKILL_CODE | Audio-check title fixed to "Listening practice audio check" for every practice session | practice session is writing/speaking only | COSMETIC |
| src/WebMVC/Views/Placement/Result.cshtml:108 | SKILL_CODE | Stat card hardcoded "Listening", bound to `Model.Listening` | certificate whose placement skills differ | WRONG_DISPLAY |
| src/WebMVC/Views/Placement/Result.cshtml:113 | SKILL_CODE | Stat card hardcoded "Reading" | certificate whose placement skills differ | WRONG_DISPLAY |
| src/WebMVC/Views/Placement/Index.cshtml:137 | BAND_CODE | "Your score, CEFR band, and per-section breakdown" | certificate whose bands are not CEFR | COSMETIC |
| src/WebMVC/Views/Placement/_TakeSubmitModal.cshtml:11 | BAND_CODE | "your starting CEFR band calculated" | non-CEFR certificate | COSMETIC |
| src/WebMVC/Views/Speaking/Review.cshtml:56 | SCORE_SCALE | "Speaking score · 0–200 scale" — 200 ceiling as a literal string | speaking section scaled to anything other than 0–200 | WRONG_DISPLAY |
| src/WebMVC/Views/Speaking/Review.cshtml:234 | BAND_CODE | Copy asserts a band is only ever derived from a full multi-skill exam | certificate that reports a per-skill band | COSMETIC |
| src/WebMVC/Views/Writing/Review.cshtml:66 | BAND_CODE | "No band is shown for writing alone" | certificate with per-skill bands | COSMETIC |
| src/WebMVC/Views/Exams/History.cshtml:36 | BAND_CODE | Empty state promises "CEFR proficiency" tracking | non-CEFR certificate | COSMETIC |
| src/WebMVC/Views/Exams/History.cshtml:63 | BAND_CODE | Card header "Equivalent CEFR Level" printed for every cert | TOEIC/VSTEP band scheme | WRONG_DISPLAY |
| src/WebMVC/Views/Exams/History.cshtml:83 | BAND_CODE | "CEFR thresholds are fixed by the certification standard" | thresholds are admin-editable and may have been changed | WRONG_DISPLAY |
| src/WebMVC/Views/Exams/_TakeModals.cshtml:41 | BAND_CODE | Submit modal promises a "scaled band computed immediately" | cert using the `independent` aggregation rule (no composite band) | WRONG_DISPLAY |
| src/WebMVC/Views/Exams/_TakeModals.cshtml:169 | SKILL_CODE | Review-screen modal titled "READING REVIEW SCREEN" | the phase under review is listening/speaking/writing | WRONG_DISPLAY |
| src/WebMVC/Views/Exams/_PreflightHeader.cshtml:20 | SKILL_CODE | Badge "SPEAKING EXAM" shown whenever `RequiresMicrophone` | a writing/oral-interview cert needs a mic without being speaking | COSMETIC |
| src/WebMVC/Views/Exams/_TakeStimulus.cshtml:6 | SKILL_CODE | Split-pane header "READING PASSAGE & STIMULUS" | stimulus shown for a listening or writing item | COSMETIC |
| src/WebMVC/Views/Exams/Review.cshtml:45 | SECTION_CODE | Filter label "Part / Section" assumes the part vocabulary | — | COSMETIC |
| src/WebMVC/ViewModels/Exams/ExamTakePresentation.cs:63 | SKILL_CODE | Audio player `SectionTitle` fixed to "Listening Audio" for every audio-bearing question | speaking-prompt or writing audio | COSMETIC |
| src/WebMVC/ViewModels/Exams/ExamPreflightViewModel.cs:23 | SKILL_CODE | Buffer detail "No listening audio tracks in this exam blueprint" | exam has speaking audio but no listening | COSMETIC |
| src/WebMVC/ViewModels/Placement/PlacementTakeViewModel.cs:101 | SKILL_CODE | Player section title "Diagnostic Listening Audio" for any audio question | non-listening audio in placement | COSMETIC |
| src/WebMVC/ViewModels/Admin/Scoring/AdminScoreConversionViewModel.cs:11 | SCORE_SCALE | Preview ceiling falls back to 100 questions when `MaxQuestions` is 0 | scale with a different raw ceiling | WRONG_DISPLAY |
| src/WebMVC/ViewModels/Admin/Scoring/AdminScoreConversionViewModel.cs:12 | SCORE_SCALE | Preview sample defaults to `MaxQuestions * 3 / 4` | a 0–9 or 0–10 band scale where 75% of raw count is meaningless | WRONG_DISPLAY |
| src/WebMVC/ViewModels/Exams/ExamEditorPresentation.cs:12 | SKILL_CODE | `TotalListening`/`Percent` count only Listening-modality sections, then drive the audio-completion percentage | certificate with required audio outside the listening skill | WRONG_DISPLAY |
| src/WebMVC/Mappers/AttemptViewModelMapper.cs:71 | SCORE_SCALE | `MaximumScore` fallback literal 990 when the cert has no bands | non-TOEIC certificate with missing band data shows "out of 990" | WRONG_DISPLAY |
| src/WebMVC/Mappers/AttemptViewModelMapper.cs:35 | SCORE_SCALE | Chart ceiling falls back to `BestScore ?? 100` | no bands and no best score: bar heights scale against an invented 100 | WRONG_DISPLAY |
| src/WebMVC/Mappers/AttemptViewModelMapper.cs:72 | SKILL_CODE | Listening located by `SkillCode == "LIST"` or name containing "Listening" | skill code renamed to e.g. "LIS" or labelled "Auditory Comprehension" — placement shows 0 | BLOCKER |
| src/WebMVC/Mappers/AttemptViewModelMapper.cs:73 | SKILL_CODE | Reading located by `SkillCode == "READ"` or name containing "Reading" | code or label renamed — placement shows 0 | BLOCKER |
| src/WebMVC/Mappers/AttemptViewModelMapper.cs:90 | BAND_CODE | Special-cases band code `"below_A1"` to render "<A1" | different band-code scheme (e.g. "PreA1", or a non-CEFR cert) leaks the raw code to learners | WRONG_DISPLAY |
| src/WebMVC/wwwroot/js/exams/navigation.js:5 | SKILL_CODE | `phase.skillCode === 'LIST'` decides one-way listening navigation | any cert whose listening code is not exactly LIST | BLOCKER |
| src/WebMVC/wwwroot/js/exams/navigation.js:6 | SKILL_CODE | `phase.skillCode === 'READ'` decides the flagged filter and Review Screen button | non-READ reading code — review screen never appears | BLOCKER |
| src/WebMVC/wwwroot/js/exams/navigation.js:444 | SKILL_CODE | Backwards-navigation guard hardcoded to `'LIST'` | new listening-like skill code lets candidates rewind audio | WRONG_DISPLAY |
| src/WebMVC/wwwroot/js/keyboard-nav.js:2 | CERT_NAME | File header describes the "TOEIC Practice exam interface" | — | COSMETIC |
| src/WebMVC/wwwroot/js/components/ai-insight-panel.js:150 | BAND_CODE | Recommendation chip renders `Band ${targetBandCode}` for any certificate | cert whose levels are not bands | COSMETIC |
| src/WebMVC/wwwroot/css/components.css:1264 | BAND_CODE | CEFR bar documented and laid out for fixed thresholds A1 120 · A2 225 · B1 550 · B2 785 · C1 945 on the 10–990 scale | any certificate not on 10–990 (VSTEP 0–10, IELTS 0–9) | WRONG_DISPLAY |
| src/WebMVC/wwwroot/css/components.css:1284 | BAND_CODE | Track columns `110fr 105fr 325fr 235fr 160fr 45fr` = six CEFR bands over the TOEIC range | cert has 4/5/7 bands or a different range — band widths misrepresent the scale | WRONG_DISPLAY |
| src/WebMVC/wwwroot/css/components.css:1304 | BAND_CODE | Tick row repeats the same six-column template | same as above; score ticks stop lining up with band edges | WRONG_DISPLAY |
| src/WebMVC/wwwroot/css/components.css:1478 | BAND_CODE | Mobile override `repeat(6, …)` hardcodes six bands | band count ≠ 6 on small screens | WRONG_DISPLAY |
| src/WebMVC/wwwroot/css/components.css:284 | SECTION_CODE | "Question navigator (200 questions)" layout note assumes the 200-question TOEIC sitting | cert with a different question count | COSMETIC |
| src/WebMVC/wwwroot/css/components.css:2186 | CERT_NAME | Rule comment "Given words for TOEIC Writing Q1-5" bakes a cert + part range into the style ownership | another cert reuses `.given-words` | COSMETIC |
| src/WebMVC/wwwroot/css/dashboard/simulator.css:232 | BAND_CODE | `repeat(@sim.Bands.Count, …)` left as a literal Razor token inside a static .css file — never evaluated | always: invalid declaration, band strip collapses to one implicit column for every cert | WRONG_DISPLAY |
| src/WebMVC/ViewModels/Placement/TargetBandViewModel.cs:11 | BAND_CODE | Fallback target code literal `"B2"` | certificate has no B2 band — target preset to a non-existent code | WRONG_DISPLAY |
| src/WebMVC/ViewModels/Dashboard/DashboardSimulatorViewModel.cs:30 | BAND_CODE | Unset target defaults to `sim.Bands[^1].Code` (assumes highest band is the sensible default goal) | cert whose top band is unattainable or unordered | COSMETIC |
| src/WebMVC/Views/Dashboard/_DashboardSimulator.cshtml:162 | SCORE_SCALE | "RANGE: `@sim.Bands[0].MinTotal`–`@sim.MaxTotalScore` PTS" assumes index 0 is the lowest band | bands delivered out of order — wrong range printed | WRONG_DISPLAY |
| src/WebMVC/Views/Dashboard/_DashboardReadiness.cshtml:37 | BAND_CODE | Band name falls back to the literal string "Graded standing" | cert with no band naming | COSMETIC |
| src/WebMVC/Views/Dashboard/Index.cshtml:57 | BAND_CODE | "receive your scaled score and band" | cert with independent per-skill reporting | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep1.cshtml:44 | CERT_NAME | Code placeholder "e.g. IELTS, TOEIC" | — | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep1.cshtml:50 | CERT_NAME | Name placeholder "e.g. IELTS Academic & General" | — | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep1.cshtml:64 | SCORE_SCALE | Option label "sum (TOEIC standard sum 10–990)" | admin is authoring VSTEP/IELTS — the only offered gloss is a TOEIC range | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep1.cshtml:65 | SCORE_SCALE | Option label "avg_half_step (IELTS 0.5-step band)" | 0.5 step used by a non-IELTS cert | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep1.cshtml:66 | SCORE_SCALE | Option label "avg_whole (VSTEP whole band 0–10)" | whole-step cert not on 0–10 | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:10 | CERT_NAME | Intro cites "IELTS Writing Task 2 weight = 2.0" as the worked example | — | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:24 | SCORE_SCALE | "Sum of skill scores — e.g. TOEIC 10–990" | non-TOEIC sum cert | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:25 | SCORE_SCALE | "rounded to nearest 0.5 band — e.g. IELTS 0–9.0 / VSTEP 0–10.0" | cert with a different band ceiling | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:26 | SCORE_SCALE | "nearest whole number — e.g. VSTEP 0–10" | whole-step cert not on 0–10 | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:27 | SCORE_SCALE | "Report each skill independently — e.g. TOEIC S&W 0–200" | independent cert with another ceiling | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:32 | SCORE_SCALE | Rounding help text hardcodes the .0 / .5 / next-whole ladder for every aggregation rule | a cert rounding to 0.25 steps or to integer tens | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:84 | OTHER | Section score-weight input capped `max="10.0"`, `step="0.1"` | a weighting model needing >10× or finer than 0.1 | WRONG_DISPLAY |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:93 | OTHER | "Expected items" falls back to 10 when `ExpectedQuestionCount` is null | section genuinely has a different planned count | COSMETIC |
| src/WebMVC/Views/Admin/_CertificateWizardStep4.cshtml:143 | OTHER | Live-preview max falls back to 10 items | same | COSMETIC |
| src/WebMVC/Views/Admin/_ExamGenerateDialog.cshtml:28 | CERT_NAME | Title placeholder "e.g. TOEIC Reading Form B (Generated)" | — | COSMETIC |
| src/WebMVC/Views/Admin/_ExamGenerateDialog.cshtml:42 | BAND_CODE | Target-band select offers only B1/B2/C1 (B2 preselected) | certificate with A1/A2/C2 bands, or a non-CEFR band scheme — targets a band the cert lacks | WRONG_DISPLAY |
| src/WebMVC/Views/Admin/_ExamGenerateDialog.cshtml:61 | SECTION_CODE | Blueprint placeholder pins `certCode: TOEIC` and `sectionCode: "P5"` | non-TOEIC / non-P section code copied verbatim into a real blueprint | COSMETIC |
| src/WebMVC/Views/Admin/_ScoreConversionBands.cshtml:7 | BAND_CODE | Header "Proficiency Bands / CEFR Reference (@conv.CertCode)" calls every cert's bands CEFR | TOEIC or VSTEP band set being edited | COSMETIC |
| src/WebMVC/Views/Admin/_ExamEditorStep2.cshtml:12 | SKILL_CODE | Card header "Listening audio (@totalListening parts)" — audio equated with listening | cert with audio in speaking sections | COSMETIC |
| src/WebMVC/Views/Admin/_ExamEditorStep3.cshtml:20 | SECTION_CODE | Table column header literal "Part" for every section | certificate whose sections are Sections/Tasks | COSMETIC |
| src/WebMVC/Views/Admin/MentorForm.cshtml:101 | CERT_NAME | Headline placeholder "10 years teaching TOEIC…" | — | COSMETIC |
| src/WebMVC/Views/Mentors/Confirm.cshtml:132 | SECTION_CODE | Focus-area placeholder "…Part 3-4 conversation" | non-TOEIC cert | COSMETIC |
| src/WebMVC/Views/Admin/_StudioCanvas.cshtml:182 | SECTION_CODE | Passage editor offers blank insert buttons [1]..[4] only, ids `p6-passage-*` | section allows a different blank count | COSMETIC |
| src/WebMVC/wwwroot/js/admin/exam-studio.js:831 | SECTION_CODE | `insertP6Blank`/`renderP6Preview`/`focusP6Question` and the `p6-passage-*`/`p6-preview-*` element ids bind the passage-blank editor to TOEIC Part 6 | any non-TOEIC cert — the passage editor markup and handlers do not attach | WRONG_DISPLAY |
| src/WebMVC/wwwroot/js/admin/exam-studio.js:856 | SECTION_CODE | Blank-token regex `/\[([1-9])\]/` accepts only single-digit blank markers | section with 10+ blanks in one passage | WRONG_DISPLAY |
| src/WebAPI/wwwroot/templates/lesson-template.json:5 | SKILL_CODE | Downloadable lesson template hardcodes `"skill": "reading"` | imported into a certificate with no READING skill code | WRONG_DISPLAY |

## Ghi chú

`simulator.css:232` **không phải rủi ro tiềm ẩn mà là defect đang sống**: `repeat(@sim.Bands.Count, …)` là cú pháp Razor nằm trong file `.css` tĩnh, không view nào render → luôn là khai báo CSS sai.

5 BLOCKER là những chỗ **ngừng hoạt động im lặng** với cert mới (không phải chỉ sai nhãn): empty-state của practice, hai lookup `"LIST"`/`"READ"` trong mapper, và hai check `skillCode === 'LIST'|'READ'` trong `navigation.js`.
