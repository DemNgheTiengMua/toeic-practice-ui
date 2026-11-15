import React from 'react';
import { parseSafeInlineSegments } from '../../lib/security';

interface SafeMarkdownProps {
  content: string;
  className?: string;
}

function renderInlineSegments(text: string) {
  return parseSafeInlineSegments(text).map((seg, sIdx) => {
    if (seg.type === 'bold') {
      return (
        <strong key={sIdx} className="font-semibold text-foreground">
          {seg.value}
        </strong>
      );
    }
    if (seg.type === 'italic') {
      return (
        <em key={sIdx} className="italic">
          {seg.value}
        </em>
      );
    }
    return <span key={sIdx}>{seg.value}</span>;
  });
}

export function SafeMarkdown({ content, className = '' }: SafeMarkdownProps) {
  if (!content) return null;

  // Split by double newline into block paragraphs or lists
  const rawBlocks = content.split(/\n\s*\n/);

  return (
    <div className={`space-y-3 leading-relaxed ${className}`}>
      {rawBlocks.map((block, bIdx) => {
        const trimmed = block.trim();
        if (!trimmed) return null;

        // Check if list
        const lines = trimmed.split('\n');
        const isList = lines.every((line) => line.trim().startsWith('* ') || line.trim().startsWith('- '));

        if (isList) {
          return (
            <ul key={bIdx} className="list-disc pl-5 space-y-1">
              {lines.map((line, lIdx) => {
                const itemText = line.trim().replace(/^[-*]\s+/, '');
                return <li key={lIdx}>{renderInlineSegments(itemText)}</li>;
              })}
            </ul>
          );
        }

        // Regular paragraph with potential single newlines
        return (
          <p key={bIdx} className="m-0">
            {lines.map((line, lIdx) => (
              <React.Fragment key={lIdx}>
                {lIdx > 0 && <br />}
                {renderInlineSegments(line)}
              </React.Fragment>
            ))}
          </p>
        );
      })}
    </div>
  );
}
