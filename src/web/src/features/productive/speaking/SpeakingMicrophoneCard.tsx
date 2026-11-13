import { Alert, Button, Card, Chip, Label, ListBox, Select, Spinner } from '@heroui/react';
import { Mic, MicOff, RefreshCw } from 'lucide-react';
import type { UseMicrophoneCheckResult } from './useMicrophoneCheck';

interface SpeakingMicrophoneCardProps {
  mic: UseMicrophoneCheckResult;
  compact?: boolean;
}

export function SpeakingMicrophoneCard({ mic, compact = false }: SpeakingMicrophoneCardProps) {
  const isBlocked = mic.status === 'denied' || mic.status === 'unavailable' || mic.status === 'error';

  return (
    <Card variant={compact ? 'secondary' : 'default'}>
      <Card.Header className="flex flex-row items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          {mic.status === 'ready' ? (
            <Mic className="size-4 text-success" aria-hidden="true" />
          ) : (
            <MicOff className="size-4 text-warning" aria-hidden="true" />
          )}
          <Card.Title className="text-base">Microphone check</Card.Title>
        </div>
        {mic.status === 'checking' && (
          <Chip color="default" variant="soft" size="sm">
            <Spinner size="sm" />
            Checking device
          </Chip>
        )}
        {mic.status === 'ready' && (
          <Chip color="success" variant="soft" size="sm">
            Microphone ready
          </Chip>
        )}
        {isBlocked && (
          <Chip color={mic.status === 'denied' || mic.status === 'error' ? 'danger' : 'warning'} variant="soft" size="sm">
            Action needed
          </Chip>
        )}
      </Card.Header>

      <Card.Content className="space-y-4">
        {isBlocked && (
          <Alert role="alert" status={mic.status === 'denied' || mic.status === 'error' ? 'danger' : 'warning'}>
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>{mic.errorTitle ?? 'Microphone check failed'}</Alert.Title>
              <Alert.Description>
                {mic.errorMessage ?? 'Check your microphone connection and browser permission, then retry.'}
              </Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          {mic.devices.length > 0 && (
            <Select
              className="w-full sm:max-w-xs"
              placeholder="Choose a microphone"
              value={mic.selectedDeviceId}
              onChange={(key) => {
                if (key !== null) {
                  mic.setSelectedDeviceId(String(key));
                }
              }}
            >
              <Label>Microphone device</Label>
              <Select.Trigger>
                <Select.Value />
                <Select.Indicator />
              </Select.Trigger>
              <Select.Popover>
                <ListBox>
                  {mic.devices.map((device) => (
                    <ListBox.Item key={device.deviceId} id={device.deviceId} textValue={device.label}>
                      {device.label}
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                  ))}
                </ListBox>
              </Select.Popover>
            </Select>
          )}

          <Button
            variant={isBlocked ? 'primary' : 'secondary'}
            size="sm"
            isPending={mic.status === 'checking'}
            onPress={() => {
              void mic.retry();
            }}
          >
            <RefreshCw className="size-4" aria-hidden="true" />
            Retry microphone check
          </Button>
        </div>
      </Card.Content>
    </Card>
  );
}
