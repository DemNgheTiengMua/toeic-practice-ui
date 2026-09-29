#!/bin/bash
# Add context bars to remaining student screens

CONTEXT_BAR='        <!-- Context bar (sticky) -->
        <div class="context-bar">
          <div class="context-bar__item">
            <span class="context-bar__label">Certificate:</span>
            <span class="context-bar__value">TOEIC</span>
          </div>
          <div class="context-bar__item">
            <span class="context-bar__label">Credits:</span>
            <span class="context-bar__value">9 left</span>
          </div>
        </div>

'

# List of files that need context bars (excluding dashboard, exam-list, exam-result which are done)
FILES=(
  "prototype/student/exam-history.html"
  "prototype/student/practice-select.html"
  "prototype/student/practice-take.html"
  "prototype/student/weakness-analysis.html"
  "prototype/student/learning-path.html"
  "prototype/student/mentor-list.html"
  "prototype/student/placement-select.html"
  "prototype/payment/pricing.html"
  "prototype/payment/wallet.html"
)

for file in "${FILES[@]}"; do
  if [ -f "$file" ]; then
    # Check if context bar already exists
    if ! grep -q "context-bar" "$file"; then
      # Insert after <main class="app-main">
      sed -i '/<main class="app-main">/a\'"$CONTEXT_BAR" "$file"
      echo "Added context bar to $file"
    else
      echo "Context bar already exists in $file"
    fi
  fi
done
