import React from 'react';
import { Pagination } from '@heroui/react';

export interface OpsPaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  itemLabel: string;
  onPageChange: (page: number) => void;
}

function buildPageTokens(currentPage: number, totalPages: number): Array<number | 'ellipsis-left' | 'ellipsis-right'> {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }
  const tokens: Array<number | 'ellipsis-left' | 'ellipsis-right'> = [1];
  const windowStart = Math.max(2, currentPage - 1);
  const windowEnd = Math.min(totalPages - 1, currentPage + 1);

  if (windowStart > 2) {
    tokens.push('ellipsis-left');
  }
  for (let p = windowStart; p <= windowEnd; p += 1) {
    tokens.push(p);
  }
  if (windowEnd < totalPages - 1) {
    tokens.push('ellipsis-right');
  }
  tokens.push(totalPages);
  return tokens;
}

/**
 * Shared HeroUI v3 Pagination bar for admin operations and finance tables.
 */
export function OpsPagination({
  page,
  pageSize,
  totalCount,
  itemLabel,
  onPageChange,
}: OpsPaginationProps) {
  if (totalCount <= pageSize) {
    return null;
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const start = (safePage - 1) * pageSize + 1;
  const end = Math.min(safePage * pageSize, totalCount);
  const tokens = buildPageTokens(safePage, totalPages);

  return (
    <Pagination aria-label={`${itemLabel} pagination`}>
      <Pagination.Summary className="font-mono-tabular">
        Showing {start}–{end} of {totalCount} {itemLabel}
      </Pagination.Summary>
      <Pagination.Content>
        <Pagination.Item>
          <Pagination.Previous
            isDisabled={safePage <= 1}
            onPress={() => onPageChange(Math.max(1, safePage - 1))}
          >
            <Pagination.PreviousIcon />
            <span>Previous</span>
          </Pagination.Previous>
        </Pagination.Item>
        {tokens.map((token) =>
          typeof token === 'number' ? (
            <Pagination.Item key={token}>
              <Pagination.Link
                isActive={token === safePage}
                onPress={() => onPageChange(token)}
              >
                {token}
              </Pagination.Link>
            </Pagination.Item>
          ) : (
            <Pagination.Item key={token}>
              <Pagination.Ellipsis />
            </Pagination.Item>
          ),
        )}
        <Pagination.Item>
          <Pagination.Next
            isDisabled={safePage >= totalPages}
            onPress={() => onPageChange(Math.min(totalPages, safePage + 1))}
          >
            <span>Next</span>
            <Pagination.NextIcon />
          </Pagination.Next>
        </Pagination.Item>
      </Pagination.Content>
    </Pagination>
  );
}
