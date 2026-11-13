import React from 'react';
import { Card, Chip, Table } from '@heroui/react';

interface CostRow {
  id: string;
  action: string;
  cost: string;
  isFree?: boolean;
}

const SIGN_IN_ROWS: CostRow[] = [
  { id: 'placement', action: 'Placement exam (first per certificate)', cost: 'Free', isFree: true },
  { id: 'practice', action: 'Practice by section, any skill', cost: 'Free', isFree: true },
  { id: 'path', action: 'Learning path', cost: 'Free', isFree: true },
  { id: 'weakness', action: 'Weakness analysis', cost: 'Free', isFree: true },
  { id: 'mock', action: 'Start a mock exam', cost: '1 credit' },
  { id: 'grading', action: 'AI grading of speaking or writing', cost: '1 credit' },
  { id: 'mentor', action: 'Book a mentor session', cost: '1 credit' },
  { id: 'retake', action: 'Placement retake (4 skills / L+R)', cost: '5 / 3 credits' },
];

export function AuthCostSummaryCard({
  title = 'What is free and what costs credits',
  footerNote = 'Credits do not expire. Start with the free tools and buy credits only when you want a mock exam or a graded answer.',
}: {
  title?: string;
  footerNote?: string;
}) {
  return (
    <Card className="h-fit">
      <Card.Header>
        <Card.Title>{title}</Card.Title>
      </Card.Header>
      <Card.Content className="flex flex-col gap-4">
        <Table>
          <Table.ScrollContainer>
            <Table.Content aria-label={title}>
              <Table.Header>
                <Table.Column isRowHeader>Action</Table.Column>
                <Table.Column>Cost</Table.Column>
              </Table.Header>
              <Table.Body>
                {SIGN_IN_ROWS.map((row) => (
                  <Table.Row key={row.id}>
                    <Table.Cell className="text-sm">{row.action}</Table.Cell>
                    <Table.Cell className="font-mono-tabular text-sm whitespace-nowrap">
                      {row.isFree ? (
                        <Chip size="sm" color="success" variant="soft">
                          {row.cost}
                        </Chip>
                      ) : (
                        row.cost
                      )}
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Content>
          </Table.ScrollContainer>
        </Table>
        <p className="text-xs text-muted m-0">{footerNote}</p>
      </Card.Content>
    </Card>
  );
}
