import React, { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  Label,
  Spinner,
  TextArea,
  TextField,
} from '@heroui/react';
import { AlertTriangle, ArrowLeft, Check, Trash2 } from 'lucide-react';
import { api } from '@/lib/api';
import { toBrowserMediaUrl } from '@/lib/security';
import { PracticeAudioPane } from './PracticeAudioPane';
import {
  formatLocalReopenTime,
  formatSavedSourceLabel,
  getPracticeErrorMessage,
  readSavedNote,
  removeSavedNote,
  writeSavedNote,
} from './practiceUtils';

export function SavedQuestionDetailPage() {
  const { savedQuestionId } = useParams({ strict: false }) as { savedQuestionId?: string };
  const numericId = Number(savedQuestionId);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const detailQuery = useQuery({
    queryKey: ['saved-questions', 'detail', numericId],
    queryFn: () => api.practice.savedQuestion(numericId),
    enabled: Number.isInteger(numericId) && numericId > 0,
  });

  const [noteDraft, setNoteDraft] = useState('');
  const [noteSavedMsg, setNoteSavedMsg] = useState(false);

  useEffect(() => {
    const item = detailQuery.data;
    if (!item) return;
    setNoteDraft(
      readSavedNote({
        savedQuestionId: item.savedQuestionId,
        questionId: item.questionId,
        itemVersionId: item.itemVersionId,
      }),
    );
  }, [detailQuery.data]);

  const removeMutation = useMutation({
    mutationFn: async () => {
      const item = detailQuery.data;
      if (!item) return;
      await api.practice.unsaveQuestion(item.savedQuestionId);
      removeSavedNote({
        savedQuestionId: item.savedQuestionId,
        questionId: item.questionId,
        itemVersionId: item.itemVersionId,
      });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['saved-questions'] });
      navigate({ to: '/saved-questions' });
    },
  });

  if (detailQuery.isLoading) {
    return (
      <Card className="p-8">
        <div className="flex items-center justify-center gap-3 text-sm text-muted">
          <Spinner size="sm" />
          <span>Loading saved question…</span>
        </div>
      </Card>
    );
  }

  if (detailQuery.isError || !detailQuery.data) {
    return (
      <Alert status="danger">
        <Alert.Indicator />
        <Alert.Content>
          <Alert.Title>Saved question not found</Alert.Title>
          <Alert.Description>
            {getPracticeErrorMessage(detailQuery.error, 'This bookmark may have been removed.')}
          </Alert.Description>
          <div className="mt-3">
            <Link to="/saved-questions" className="no-underline">
              <Button size="sm" variant="secondary">
                Back to saved questions
              </Button>
            </Link>
          </div>
        </Alert.Content>
      </Alert>
    );
  }

  const item = detailQuery.data;
  const imageUrl = toBrowserMediaUrl(item.imagePath);
  const savedAtLabel = formatLocalReopenTime(item.savedAt);

  const handleSaveNote = () => {
    writeSavedNote(
      {
        savedQuestionId: item.savedQuestionId,
        questionId: item.questionId,
        itemVersionId: item.itemVersionId,
      },
      noteDraft,
    );
    setNoteSavedMsg(true);
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-separator pb-4">
        <div className="flex flex-wrap items-center gap-2">
          <Link to="/saved-questions" className="no-underline">
            <Button size="sm" variant="ghost">
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              <span>Saved questions</span>
            </Button>
          </Link>
          <h1 className="font-display text-xl font-bold text-foreground">
            {item.scopeName || item.sectionName || item.topicName || 'Saved question review'}
          </h1>
          <Chip size="sm" variant="soft" color={item.source === 'incorrect' ? 'danger' : 'accent'}>
            <Chip.Label>{formatSavedSourceLabel(item.source)}</Chip.Label>
          </Chip>
          {savedAtLabel && (
            <span className="font-mono-tabular text-xs text-muted">Saved {savedAtLabel}</span>
          )}
        </div>

        <Button
          size="sm"
          variant="danger-soft"
          isDisabled={removeMutation.isPending}
          onPress={() => removeMutation.mutate()}
        >
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Remove bookmark</span>
        </Button>
      </header>

      {item.isRetiredDefective && (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title className="inline-flex items-center gap-1.5">
              <AlertTriangle className="h-4 w-4" aria-hidden="true" />
              <span>Withdrawn as defective</span>
            </Alert.Title>
            <Alert.Description>
              This pool item was withdrawn as defective. Do not rely on its marked option key for study.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      <Card className="border border-border bg-surface">
        <Card.Header>
          {item.categoryName && (
            <div className="mb-1">
              <Chip size="sm" variant="soft" color="accent">
                <Chip.Label>{item.categoryName}</Chip.Label>
              </Chip>
            </div>
          )}
          <Card.Title className="text-lg">{item.stem}</Card.Title>
        </Card.Header>
        <Card.Content className="space-y-4">
          {imageUrl && (
            <img
              src={imageUrl}
              alt={item.stem}
              className="max-h-72 rounded-md border border-border object-contain bg-surface-secondary"
            />
          )}
          <PracticeAudioPane audioPath={item.audioPath} transcript={item.transcript} />
          {item.passage && (
            <div className="whitespace-pre-line rounded-md border border-border bg-surface-secondary p-4 text-sm leading-relaxed text-foreground">
              {item.passage}
            </div>
          )}
          {item.options.length > 0 && (
            <ul className="space-y-2">
              {item.options.map((opt) => (
                <li
                  key={opt.optionId}
                  className={`rounded-md border px-3 py-2 text-sm ${
                    opt.isCorrect && !item.isRetiredDefective
                      ? 'border-success bg-success/10 font-medium text-foreground'
                      : 'border-border bg-surface-secondary text-foreground'
                  }`}
                >
                  <strong className="font-mono-tabular mr-2">{opt.label}.</strong>
                  {opt.text}
                </li>
              ))}
            </ul>
          )}
          {item.explanation && (
            <div className="rounded-md border border-border bg-surface-secondary p-4 text-sm leading-relaxed text-foreground">
              <strong>Explanation:</strong> {item.explanation}
            </div>
          )}
        </Card.Content>
      </Card>

      <Card className="border border-border bg-surface">
        <Card.Header>
          <Card.Title className="text-base">Personal study note</Card.Title>
        </Card.Header>
        <Card.Content className="space-y-3">
          <TextField
            aria-label="Personal study note"
            value={noteDraft}
            onChange={(val) => {
              setNoteDraft(val);
              setNoteSavedMsg(false);
            }}
          >
            <Label className="sr-only">Personal study note</Label>
            <TextArea rows={3} placeholder="Add a reminder for review..." />
            <Description>
              {noteSavedMsg ? 'Saved in this browser.' : 'Stored locally in this browser.'}
            </Description>
          </TextField>
          <Button size="sm" variant="primary" onPress={handleSaveNote}>
            <Check className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Save note</span>
          </Button>
        </Card.Content>
      </Card>
    </div>
  );
}
