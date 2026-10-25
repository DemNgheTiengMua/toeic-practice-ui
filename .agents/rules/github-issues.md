---
trigger: always_on
description: Native GitHub issue tracking rules: sub-issues, tasklists, and blocker dependencies.
---

# GitHub Issue Tracking & Hierarchy Rules

When publishing specs and vertical slice tickets to GitHub via `gh`:

## 1. Native Sub-Issues & Tasklists (Progress Bar `xx/total tasks`)
- **Parent spec body**: Always include a ````[tasklist]```` block linking child issues:
  ````markdown
  ```[tasklist]
  ### Implementation Tasks
  - [ ] #<child_1>
  - [ ] #<child_2>
  ```
  ````
- **Native Sub-issue link**: Immediately wire child issues to the parent via GraphQL mutation `addSubIssue`:
  ```bash
  gh api graphql -F issueId="<parent_node_id>" -F subIssueId="<child_node_id>" \
    -f query='mutation($issueId: ID!, $subIssueId: ID!) { addSubIssue(input: { issueId: $issueId, subIssueId: $subIssueId }) { issue { id } } }'
  ```

## 2. Native Blockers (`addBlockedBy`)
- Never rely solely on markdown text for "Blocked by". Wire the native dependency via GraphQL mutation `addBlockedBy`:
  ```bash
  gh api graphql -F issueId="<gated_node_id>" -F blockingIssueId="<blocker_node_id>" \
    -f query='mutation($issueId: ID!, $blockingIssueId: ID!) { addBlockedBy(input: { issueId: $issueId, blockingIssueId: $blockingIssueId }) { issue { id } } }'
  ```

## 3. Node ID Batch Lookup
- Fetch GraphQL Node IDs for a batch of issues in one shot:
  ```bash
  gh api graphql -F owner=":owner" -F name=":repo" \
    -f query='query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { i106: issue(number: 106) { id } i107: issue(number: 107) { id } } }'
  ```
