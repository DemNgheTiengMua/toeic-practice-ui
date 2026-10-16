import json
import zipfile
import os
import struct

def make_wav():
    sample_rate = 16000
    num_samples = sample_rate
    num_channels = 1
    bits_per_sample = 16
    byte_rate = sample_rate * num_channels * bits_per_sample // 8
    block_align = num_channels * bits_per_sample // 8
    data_size = num_samples * block_align
    chunk_size = 36 + data_size
    
    header = struct.pack('<4sI4s4sIHHIIHH4sI',
        b'RIFF', chunk_size, b'WAVE',
        b'fmt ', 16, 1, num_channels, sample_rate, byte_rate, block_align, bits_per_sample,
        b'data', data_size
    )
    return header + (b'\x00' * data_size)

minimal_png = (
    b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x02'
    b'\x00\x00\x00\x90wS\xde\x00\x00\x00\x0cIDATx\x9cc\xf8\xcf\xc0\x00\x00\x03\x01'
    b'\x01\x00\x18\xdd\x8d\xb0\x00\x00\x00\x00IEND\xaeB`\x82'
)

minimal_jpg = bytes.fromhex(
    'ffd8ffe000104a46494600010101004800480000ffdb004300080606070605080707070909'
    '080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30'
    '313434341f27393d38323c2e333430ffc0000b080001000101011100ffc4001f0000010501'
    '010101010100000000000000000102030405060708090a0bffda0008010100003f007f00ff'
    'd9'
)

out_dir = os.path.join(os.path.dirname(__file__), '..', 'docs', 'samples')
os.makedirs(out_dir, exist_ok=True)

# 1. Standalone TOEIC Reading JSON (Part 5, 6, 7)
toeic_text_manifest = {
  "version": "1.0",
  "exam": {
    "title": "TOEIC Reading Mock Form A (Text Ingest)",
    "purpose": "mock",
    "durationMinutes": 75,
    "sourceName": "ETS Reading Sample 2026",
    "description": "Standard Reading evaluation covering Part 5, Part 6, and Part 7."
  },
  "sections": [
    {
      "sectionCode": "P5",
      "groups": [
        {
          "displayOrder": 1,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "The regional sales conference will commence promptly _____ 9:00 AM in the Grand Ballroom.",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "grammar_prep",
              "explanation": "The preposition 'at' is used before precise clock times.",
              "hint": "Identify the preposition used for specific hours.",
              "options": [
                { "label": "A", "text": "at", "isCorrect": True, "rationale": "Correct preposition for clock times (at 9:00 AM)." },
                { "label": "B", "text": "on", "isCorrect": False, "rationale": "'On' is used with specific days or dates, not clock times." },
                { "label": "C", "text": "in", "isCorrect": False, "rationale": "'In' is used with months, years, or long periods." },
                { "label": "D", "text": "for", "isCorrect": False, "rationale": "'For' indicates duration, not starting point." }
              ]
            },
            {
              "displayOrder": 2,
              "stem": "Ms. Reynolds has _____ submitted her finalized quarterly expense report to accounting.",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "grammar_pos",
              "explanation": "An adverb is required between the auxiliary 'has' and past participle 'submitted'.",
              "hint": "Choose the adverb modifying the verb.",
              "options": [
                { "label": "A", "text": "already", "isCorrect": True, "rationale": "'Already' fits between auxiliary and past participle." },
                { "label": "B", "text": "ready", "isCorrect": False, "rationale": "Adjective cannot modify the verb phrase here." },
                { "label": "C", "text": "readiness", "isCorrect": False, "rationale": "Noun cannot occupy this adverbial position." },
                { "label": "D", "text": "readily", "isCorrect": False, "rationale": "Semantically awkward in this context compared to 'already'." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P6",
      "groups": [
        {
          "displayOrder": 2,
          "passage": "To All Branch Employees:\nPlease be advised that the annual IT security audit will take place next Monday. All staff members must update their workstation passwords before Friday afternoon. Anyone experiencing technical difficulties should contact the helpdesk immediately.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "What action are employees required to take before Friday?",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "reading_vocab",
              "explanation": "The memo instructs all staff members to update their workstation passwords before Friday.",
              "hint": "Look for the sentence containing 'Friday afternoon'.",
              "options": [
                { "label": "A", "text": "Update their workstation passwords", "isCorrect": True, "rationale": "Directly stated in the notice." },
                { "label": "B", "text": "Attend a mandatory security lecture", "isCorrect": False, "rationale": "No lecture is mentioned." },
                { "label": "C", "text": "Submit new hardware requisitions", "isCorrect": False, "rationale": "Not referenced in the text." },
                { "label": "D", "text": "Install operating system patches manually", "isCorrect": False, "rationale": "Password update is the only stated requirement." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P7",
      "groups": [
        {
          "displayOrder": 3,
          "passage": "GREENWAY LOGISTICS ANNOUNCEMENT\nGreenway Logistics is pleased to announce the opening of our new distribution center in Da Nang. This 50,000-square-meter facility features automated sorting and temperature-controlled storage, allowing us to reduce delivery times across Central Vietnam by up to 35 percent. Customer service inquiries should be routed through our regional hotline.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "What is the primary benefit of the new Da Nang facility?",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "reading_inference",
              "explanation": "The announcement states delivery times across Central Vietnam will be reduced by up to 35%.",
              "hint": "Check the percentage reduction stated in the second sentence.",
              "options": [
                { "label": "A", "text": "Faster shipping across Central Vietnam", "isCorrect": True, "rationale": "Directly matches 'reduce delivery times by up to 35 percent'." },
                { "label": "B", "text": "Lower warehouse rental fees for partners", "isCorrect": False, "rationale": "No rental pricing is discussed." },
                { "label": "C", "text": "Immediate international flight connections", "isCorrect": False, "rationale": "The facility is a domestic distribution center." },
                { "label": "D", "text": "Expanded customer hotline operating hours", "isCorrect": False, "rationale": "Customer hotline is mentioned, but hours are not changed." }
              ]
            }
          ]
        }
      ]
    }
  ]
}

with open(os.path.join(out_dir, 'sample-exam-toeic-2skill.json'), 'w', encoding='utf-8') as f:
    json.dump(toeic_text_manifest, f, indent=2, ensure_ascii=False)
print("Created sample-exam-toeic-2skill.json")

# 2. Full TOEIC 2-Skill Package ZIP (Parts 1-7 with Audio and Image)
toeic_full_manifest = {
  "version": "1.0",
  "exam": {
    "title": "TOEIC Full 2-Skill Mock Test 2026 (Listening & Reading)",
    "purpose": "mock",
    "durationMinutes": 120,
    "sourceName": "ETS Official Full Simulation",
    "description": "Comprehensive 2-skill mock exam including audio recordings and photographs for all parts."
  },
  "sections": [
    {
      "sectionCode": "P1",
      "groups": [
        {
          "displayOrder": 1,
          "passage": None,
          "audioFile": "media/p1-photo-01.wav",
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": "media/p1-photo-01.jpg",
          "imageAlt": "An engineer wearing a safety helmet examining blueprints on a construction site.",
          "transcript": "(A) He is operating heavy machinery. (B) He is inspecting some architectural drawings. (C) He is painting a wall. (D) He is climbing a ladder.",
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Look at the photograph and choose the statement that best describes what you see.",
              "difficultyLevel": 1,
              "questionType": "mcq",
              "categoryCode": "listening_photo",
              "explanation": "Statement (B) accurately portrays the engineer examining blueprints.",
              "hint": "Focus on the documents in the engineer's hands.",
              "options": [
                { "label": "A", "text": "(A) He is operating heavy machinery.", "isCorrect": False, "rationale": "No machinery is being operated." },
                { "label": "B", "text": "(B) He is inspecting some architectural drawings.", "isCorrect": True, "rationale": "Directly matches examining blueprints." },
                { "label": "C", "text": "(C) He is painting a wall.", "isCorrect": False, "rationale": "No painting activity is occurring." },
                { "label": "D", "text": "(D) He is climbing a ladder.", "isCorrect": False, "rationale": "The person is standing on the ground." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P2",
      "groups": [
        {
          "displayOrder": 2,
          "passage": None,
          "audioFile": "media/p2-audio-01.wav",
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": "Where did you file the Henderson contract? (A) In the third drawer of the filing cabinet. (B) Yes, earlier this morning. (C) It will expire next month.",
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Where did you file the Henderson contract?",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "listening_detail",
              "explanation": "Question starts with 'Where', requiring a location response.",
              "hint": "Identify the place indicator.",
              "options": [
                { "label": "A", "text": "In the third drawer of the filing cabinet.", "isCorrect": True, "rationale": "Directly answers 'Where'." },
                { "label": "B", "text": "Yes, earlier this morning.", "isCorrect": False, "rationale": "'Yes' cannot answer WH-question; also gives time, not place." },
                { "label": "C", "text": "It will expire next month.", "isCorrect": False, "rationale": "Provides irrelevant expiration status." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P3",
      "groups": [
        {
          "displayOrder": 3,
          "passage": None,
          "audioFile": "media/p3-audio-01.wav",
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": "Speaker A: Mark, did you arrange catering for the shareholder seminar?\nSpeaker B: Yes, I contacted Golden Spoon Caterers yesterday. They will set up the buffet by 11:30 AM.",
          "questions": [
            {
              "displayOrder": 1,
              "stem": "When will the buffet be set up?",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "listening_detail",
              "explanation": "The speaker confirms the buffet will be set up by 11:30 AM.",
              "hint": "Listen for the specific time mentioned.",
              "options": [
                { "label": "A", "text": "By 11:30 AM", "isCorrect": True, "rationale": "Directly matches spoken time." },
                { "label": "B", "text": "At 1:00 PM", "isCorrect": False, "rationale": "Incorrect time." },
                { "label": "C", "text": "Tomorrow morning", "isCorrect": False, "rationale": "Not specified as tomorrow morning." },
                { "label": "D", "text": "Immediately after the keynote", "isCorrect": False, "rationale": "Event trigger not mentioned." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P5",
      "groups": [
        {
          "displayOrder": 4,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "The revised marketing strategy was praised for its _____ approach to customer engagement.",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "grammar_pos",
              "explanation": "An adjective is needed before the noun 'approach'.",
              "hint": "Select the adjective form.",
              "options": [
                { "label": "A", "text": "innovative", "isCorrect": True, "rationale": "Adjective modifying the noun 'approach'." },
                { "label": "B", "text": "innovate", "isCorrect": False, "rationale": "Verb form." },
                { "label": "C", "text": "innovation", "isCorrect": False, "rationale": "Noun form." },
                { "label": "D", "text": "innovatively", "isCorrect": False, "rationale": "Adverb form." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P7",
      "groups": [
        {
          "displayOrder": 5,
          "passage": "Notice of Facility Upgrades:\nStarting this weekend, our downtown fitness center will be undergoing equipment modernization. All strength training machines on Level 2 will be replaced with new digital models. Cardio equipment on Level 1 will remain accessible throughout the renovation.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Which area will remain open during the modernization?",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "reading_inference",
              "explanation": "The notice states cardio equipment on Level 1 will remain accessible.",
              "hint": "Look for Level 1 status.",
              "options": [
                { "label": "A", "text": "Cardio equipment on Level 1", "isCorrect": True, "rationale": "Directly matches 'accessible throughout the renovation'." },
                { "label": "B", "text": "Strength machines on Level 2", "isCorrect": False, "rationale": "Level 2 machines are being replaced." },
                { "label": "C", "text": "The rooftop swimming pool", "isCorrect": False, "rationale": "Not mentioned in the text." },
                { "label": "D", "text": "The private sauna suites", "isCorrect": False, "rationale": "Not mentioned in the text." }
              ]
            }
          ]
        }
      ]
    }
  ]
}

zip_path = os.path.join(out_dir, 'sample-exam-toeic-2skill-package.zip')
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('manifest.json', json.dumps(toeic_full_manifest, indent=2, ensure_ascii=False))
    z.writestr('media/p1-photo-01.jpg', minimal_jpg)
    z.writestr('media/p1-photo-01.wav', make_wav())
    z.writestr('media/p2-audio-01.wav', make_wav())
    z.writestr('media/p3-audio-01.wav', make_wav())
print("Created sample-exam-toeic-2skill-package.zip")

# 3. Standalone IELTS 4-Skill JSON
ielts_text_manifest = {
  "version": "1.0",
  "exam": {
    "title": "IELTS Academic Mock Test Form 01 (Text Ingest)",
    "purpose": "mock",
    "durationMinutes": 150,
    "sourceName": "Cambridge Academic Model",
    "description": "Academic test package covering Reading, Writing Task 1-2, and Speaking interview prompts."
  },
  "sections": [
    {
      "sectionCode": "PAS",
      "groups": [
        {
          "displayOrder": 1,
          "passage": "Urban Microclimates and Green Canopy Effects:\nModern metropolises frequently experience the urban heat island effect, wherein asphalt and concrete structures absorb solar radiation during daylight hours and release thermal energy after dusk. Recent observational studies in Singapore demonstrate that increasing urban canopy coverage to 40 percent reduces ambient surface temperatures by an average of 3.2 degrees Celsius.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "What temperature reduction was achieved with 40 percent urban canopy coverage?",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "ielts_detail",
              "explanation": "The text states an average reduction of 3.2 degrees Celsius.",
              "hint": "Check the numerical data in the final sentence.",
              "options": [
                { "label": "A", "text": "3.2 degrees Celsius", "isCorrect": True, "rationale": "Directly matches stated observational study." },
                { "label": "B", "text": "4.0 degrees Celsius", "isCorrect": False, "rationale": "40 percent was canopy coverage, not temperature reduction." },
                { "label": "C", "text": "1.5 degrees Celsius", "isCorrect": False, "rationale": "Not mentioned in the passage." },
                { "label": "D", "text": "5.0 degrees Celsius", "isCorrect": False, "rationale": "Not supported by text." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "T1",
      "groups": [
        {
          "displayOrder": 2,
          "passage": "The table below provides figures on international tourism expenditures in billions of USD across five countries between 2020 and 2025.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Summarise the information by selecting and reporting the main features, and make comparisons where relevant.",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": None,
              "responseSeconds": 1200,
              "minWords": 150,
              "maxWords": 220,
              "explanation": "Provide a clear overview, highlight highest and lowest expenditures, and compare trends across periods.",
              "hint": "Ensure you include an overview paragraph summarizing the general trend.",
              "criteria": [
                { "code": "TR" },
                { "code": "CC" },
                { "code": "LR" },
                { "code": "GRA" }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "T2",
      "groups": [
        {
          "displayOrder": 3,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "In many countries, an increasing number of young people choose to study abroad rather than attending domestic universities. Do the advantages of this trend outweigh the disadvantages?",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": None,
              "responseSeconds": 2400,
              "minWords": 250,
              "maxWords": 400,
              "explanation": "Discuss cultural exposure and quality education vs expenses and homesickness, stating a clear thesis throughout.",
              "hint": "Structure into Introduction, Advantage paragraph, Disadvantage paragraph, and Opinionated Conclusion.",
              "criteria": [
                { "code": "TR" },
                { "code": "CC" },
                { "code": "LR" },
                { "code": "GRA" }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "INT",
      "groups": [
        {
          "displayOrder": 4,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Part 1 (Interview): Let's talk about daily routines. What time do you usually wake up, and how do you like to start your morning?",
              "difficultyLevel": 3,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": 15,
              "responseSeconds": 45,
              "minWords": None,
              "maxWords": None,
              "explanation": "Answer with natural fluency, varied sentence structures, and appropriate time markers.",
              "hint": "Mention morning habits like exercise, coffee, or planning the day.",
              "criteria": [
                { "code": "FC" },
                { "code": "LR" },
                { "code": "GRA" },
                { "code": "PRON" }
              ]
            },
            {
              "displayOrder": 2,
              "stem": "Part 2 (Cue Card): Describe an invention that has significantly changed people's daily lives. You should say: what the invention is, when you first used it, how it benefits society, and explain whether all changes brought by it are positive.",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": 60,
              "responseSeconds": 120,
              "minWords": None,
              "maxWords": None,
              "explanation": "Deliver a continuous 2-minute speech covering all points with sophisticated vocabulary and cohesive devices.",
              "hint": "Address advantages and possible negative side effects (e.g. smartphones, internet).",
              "criteria": [
                { "code": "FC" },
                { "code": "LR" },
                { "code": "GRA" },
                { "code": "PRON" }
              ]
            }
          ]
        }
      ]
    }
  ]
}

with open(os.path.join(out_dir, 'sample-exam-ielts-4skill.json'), 'w', encoding='utf-8') as f:
    json.dump(ielts_text_manifest, f, indent=2, ensure_ascii=False)
print("Created sample-exam-ielts-4skill.json")

# 4. Full IELTS 4-Skill Package ZIP (with Listening Audio and Writing Chart Image)
ielts_full_manifest = {
  "version": "1.0",
  "exam": {
    "title": "IELTS Academic 4-Skill Complete Package 2026",
    "purpose": "mock",
    "durationMinutes": 170,
    "sourceName": "Cambridge Academic Model Package",
    "description": "Complete four-skill mock exam including listening audio recordings and writing task 1 visual chart image."
  },
  "sections": [
    {
      "sectionCode": "REC",
      "groups": [
        {
          "displayOrder": 1,
          "passage": None,
          "audioFile": "media/ielts-sec1-audio.wav",
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": "Advisor: Good morning, City Library membership services. How may I direct your call?\nCaller: Hello, I'd like to ask about registering for the weekend language exchange workshop.",
          "questions": [
            {
              "displayOrder": 1,
              "stem": "What is the caller interested in registering for?",
              "difficultyLevel": 2,
              "questionType": "mcq",
              "categoryCode": "ielts_detail",
              "explanation": "The caller asks about registering for the weekend language exchange workshop.",
              "hint": "Listen to the purpose stated by the caller.",
              "options": [
                { "label": "A", "text": "Weekend language exchange workshop", "isCorrect": True, "rationale": "Exact match with caller statement." },
                { "label": "B", "text": "Annual book borrowing subscription", "isCorrect": False, "rationale": "Caller does not ask to borrow books." },
                { "label": "C", "text": "Volunteer librarian training program", "isCorrect": False, "rationale": "No volunteer program is requested." },
                { "label": "D", "text": "Children's storytelling afternoon", "isCorrect": False, "rationale": "Different service." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "PAS",
      "groups": [
        {
          "displayOrder": 2,
          "passage": "Advances in Marine Biodiversity Mapping:\nAutonomous underwater vehicles (AUVs) equipped with multi-beam sonar have transformed marine conservation. Traditional trawling surveys sampled less than 0.1 percent of benthic habitats. Modern acoustic telemetry allows non-invasive continuous tracking of endangered cetacean populations across migratory corridors.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Why are modern acoustic telemetry techniques considered superior to traditional trawling?",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "ielts_inference",
              "explanation": "The passage notes they allow non-invasive continuous tracking compared to limited sampling.",
              "hint": "Look for adjectives describing the modern technique in the final sentence.",
              "options": [
                { "label": "A", "text": "They provide non-invasive, continuous tracking", "isCorrect": True, "rationale": "Directly matches 'non-invasive continuous tracking'." },
                { "label": "B", "text": "They cost significantly less to manufacture", "isCorrect": False, "rationale": "Costs are not mentioned." },
                { "label": "C", "text": "They capture living specimens for laboratory study", "isCorrect": False, "rationale": "The method is non-invasive and does not capture specimens." },
                { "label": "D", "text": "They operate exclusively in shallow freshwater zones", "isCorrect": False, "rationale": "They map marine benthic habitats." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "T1",
      "groups": [
        {
          "displayOrder": 3,
          "passage": "The chart illustrates annual water usage by sector in three Mediterranean regions in 2025.",
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": "media/ielts-t1-water.png",
          "imageAlt": "Bar chart comparing agricultural, industrial, and domestic water consumption.",
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Summarise the information by selecting and reporting the main features, and make comparisons where relevant.",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": None,
              "responseSeconds": 1200,
              "minWords": 150,
              "maxWords": 220,
              "explanation": "Analyze agricultural vs domestic consumption, noting high agricultural dominance.",
              "hint": "Highlight the highest and lowest proportions clearly.",
              "criteria": [
                { "code": "TR" },
                { "code": "CC" },
                { "code": "LR" },
                { "code": "GRA" }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "T2",
      "groups": [
        {
          "displayOrder": 4,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Some governments are investing heavily in public transport infrastructure, while others argue that funding should prioritize highway networks for private vehicles. Discuss both views and give your opinion.",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": None,
              "responseSeconds": 2400,
              "minWords": 250,
              "maxWords": 380,
              "explanation": "Contrast environmental benefits and congestion reduction of public transit with personal mobility of highways.",
              "hint": "Support each perspective with real-world examples before stating your final position.",
              "criteria": [
                { "code": "TR" },
                { "code": "CC" },
                { "code": "LR" },
                { "code": "GRA" }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "INT",
      "groups": [
        {
          "displayOrder": 5,
          "passage": None,
          "audioFile": None,
          "audioStartMs": None,
          "audioEndMs": None,
          "imageFile": None,
          "imageAlt": None,
          "transcript": None,
          "questions": [
            {
              "displayOrder": 1,
              "stem": "Part 1: Do you prefer reading physical books or electronic books? Why?",
              "difficultyLevel": 3,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": 15,
              "responseSeconds": 45,
              "minWords": None,
              "maxWords": None,
              "explanation": "Express personal preference with clear reasoning and contrasting vocabulary.",
              "hint": "Contrast portability/screen convenience with the tactile feel of printed pages.",
              "criteria": [
                { "code": "FC" },
                { "code": "LR" },
                { "code": "GRA" },
                { "code": "PRON" }
              ]
            },
            {
              "displayOrder": 2,
              "stem": "Part 2 (Cue Card): Describe a memorable journey you took by public transport. You should say: where you went, what kind of transport you used, who you were with, and explain why this journey was memorable to you.",
              "difficultyLevel": 4,
              "questionType": "free_response",
              "categoryCode": None,
              "prepSeconds": 60,
              "responseSeconds": 120,
              "minWords": None,
              "maxWords": None,
              "explanation": "Organize a vivid travel narrative with past narrative tenses and expressive adjectives.",
              "hint": "Describe scenic sights, memorable encounters, or unexpected events during the trip.",
              "criteria": [
                { "code": "FC" },
                { "code": "LR" },
                { "code": "GRA" },
                { "code": "PRON" }
              ]
            }
          ]
        }
      ]
    }
  ]
}

ielts_zip_path = os.path.join(out_dir, 'sample-exam-ielts-4skill-package.zip')
with zipfile.ZipFile(ielts_zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('manifest.json', json.dumps(ielts_full_manifest, indent=2, ensure_ascii=False))
    z.writestr('media/ielts-sec1-audio.wav', make_wav())
    z.writestr('media/ielts-t1-water.png', minimal_png)
print("Created sample-exam-ielts-4skill-package.zip")
