import React from 'react';
import { parseSafeInlineSegments } from '../../lib/security';

interface SafeMarkdownProps {
  content: string;
  className?: string;
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
                const segments = parseSafeInlineSegments(itemText);
                return (
                  <li key={lIdx}>
                    {segments.map((seg, sIdx) =>
                      seg.type === 'bold' ? (
                        <strong key={sIdx} className="font-semibold text-foreground">
                          {seg.value}
                        </strong>
                      ) : (
                        <span key={sIdx}>{seg.value}</span>
                      ),
                    )}
                  </li>
                );
              })}
            </ul>
          );
        }

        // Regular paragraph with potential single newlines
        return (
          <p key={bIdx} className="m-0">
            {lines.map((line, lIdx) => {
              const segments = parseSafeInlineSegments(line);
              return (
                <React.Fragment key={lIdx}>
                  {lIdx > 0 && <br />}
                  {segments.map((seg, sIdx) =>
                    seg.type === 'bold' ? (
                      <strong key={sIdx} className="font-semibold text-foreground">
                        {seg.value}
                      </strong>
                    ) : (
                      <span key={sIdx}>{seg.value}</span>
                    ),
                  )}
                </React.Fragment>
              );
            })}
          </p>
        );
      })}
    </div>
  );
}
