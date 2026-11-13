import React from 'react';
import type { CertificateBandDto } from '../../lib/api';
import { computeBandColumnTemplate, getSortedBands } from './catalogHelpers';

export function BandStrip({
  bands,
  ownerName,
}: {
  bands: readonly CertificateBandDto[];
  ownerName: string;
}) {
  const ordered = getSortedBands(bands);
  if (ordered.length === 0) {
    return null;
  }

  const columnTemplate = computeBandColumnTemplate(ordered);
  const ariaLabel = `${ownerName} score bands from ${ordered[0].code} at ${ordered[0].minTotal} to ${ordered[ordered.length - 1].code} at ${ordered[ordered.length - 1].maxTotal}`;
  const note = `${ordered[0].minTotal}–${ordered[ordered.length - 1].maxTotal} scale. Thresholds come from this certificate's own band table.`;

  return (
    <div className="flex flex-col gap-1.5">
      <div
        className="grid rounded-lg overflow-hidden border border-border bg-surface-secondary"
        role="img"
        aria-label={ariaLabel}
        style={{ gridTemplateColumns: columnTemplate }}
      >
        {ordered.map((band, index) => (
          <div
            key={band.code}
            data-band-code={band.code}
            className={`px-2 py-2 text-center font-mono-tabular text-xs font-semibold truncate ${
              index > 0 ? 'border-l border-border' : ''
            } ${index === ordered.length - 1 ? 'bg-accent/15 text-accent' : 'text-foreground'}`}
            title={`${band.code} (${band.name}): ${band.minTotal}–${band.maxTotal}`}
          >
            {band.code}
          </div>
        ))}
      </div>
      <div
        className="grid text-[11px] font-mono-tabular text-muted"
        aria-hidden="true"
        style={{ gridTemplateColumns: columnTemplate }}
      >
        {ordered.map((band) => (
          <span key={`${band.code}-tick`} className="truncate pl-0.5">
            {band.minTotal}
          </span>
        ))}
      </div>
      <p className="text-xs text-muted m-0">{note}</p>
    </div>
  );
}
