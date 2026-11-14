import React, { useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Modal, Spinner } from '@heroui/react';
import { CheckCircle2, QrCode, XCircle } from 'lucide-react';
import { usePayOS } from '@payos/payos-checkout';
import { api } from '../../lib/api';
import { formatVnd } from '../../lib/security';

export interface PayOsEmbeddedModalProps {
  isOpen: boolean;
  orderCode: string;
  checkoutUrl: string;
  amount?: number;
  credits?: number;
  returnUrl?: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export function PayOsEmbeddedModal({
  isOpen,
  orderCode,
  checkoutUrl,
  amount,
  credits,
  returnUrl,
  onClose,
  onSuccess,
}: PayOsEmbeddedModalProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [isSuccess, setIsSuccess] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);

  const safeReturnUrl =
    returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//')
      ? returnUrl
      : '/pricing';

  const handleCancelAndClose = async () => {
    setIsCancelling(true);
    try {
      exit();
    } catch {
      // ignore exit error
    }
    try {
      if (orderCode) {
        await api.finance.cancelOrder(orderCode);
      }
    } catch {
      // ignore cancel error
    } finally {
      setIsCancelling(false);
      onClose();
    }
  };

  const { open, exit } = usePayOS({
    RETURN_URL: typeof window !== 'undefined' ? window.location.href : '',
    ELEMENT_ID: 'payos-embedded-container',
    CHECKOUT_URL: checkoutUrl,
    embedded: true,
    onSuccess: (_event) => {
      setIsSuccess(true);
      void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
      void queryClient.invalidateQueries({ queryKey: ['finance', 'wallet'] });
      void queryClient.invalidateQueries({ queryKey: ['finance', 'checkout-preview'] });
      if (onSuccess) {
        onSuccess();
      }
      setTimeout(() => {
        try {
          exit();
        } catch {
          // ignore
        }
        onClose();
        void navigate({ to: safeReturnUrl });
      }, 1500);
    },
    onCancel: (_event) => {
      void handleCancelAndClose();
    },
    onExit: (_event) => {
      void handleCancelAndClose();
    },
  });

  useEffect(() => {
    if (!isOpen || !checkoutUrl || isSuccess) return;

    // Small delay ensuring container DOM element is mounted inside modal dialog
    const timer = setTimeout(() => {
      try {
        open();
      } catch (err) {
        console.error('Failed to initialize PayOS embedded checkout', err);
      }
    }, 50);

    return () => {
      clearTimeout(timer);
      try {
        exit();
      } catch {
        // ignore
      }
    };
  }, [isOpen, checkoutUrl, isSuccess]);

  return (
    <Modal.Backdrop
      isOpen={isOpen}
      onOpenChange={(openState) => {
        if (!openState && !isSuccess) {
          void handleCancelAndClose();
        }
      }}
    >
      <Modal.Container placement="center">
        <Modal.Dialog className="sm:max-w-lg w-full">
          {!isSuccess && (
            <Modal.CloseTrigger
              aria-label="Close PayOS Modal"
              onPress={() => void handleCancelAndClose()}
            />
          )}

          <Modal.Header>
            <Modal.Heading className="text-xl font-bold flex items-center gap-2">
              <QrCode className="w-5 h-5 text-accent" />
              VietQR / PayOS Checkout
            </Modal.Heading>
          </Modal.Header>

          <Modal.Body className="space-y-4">
            {isSuccess ? (
              <div
                role="status"
                aria-live="polite"
                className="flex flex-col items-center justify-center py-10 gap-3 text-center"
              >
                <CheckCircle2 className="w-16 h-16 text-success animate-bounce" />
                <h3 className="text-lg font-semibold text-foreground">Payment Successful!</h3>
                <p className="text-sm text-muted">
                  {credits ? `+${credits} credits deposited.` : 'Credits deposited.'} Redirecting to your destination...
                </p>
                <Spinner size="sm" />
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between text-xs text-muted border-b border-border pb-2">
                  <span>
                    Order Code: <strong className="font-mono text-foreground">{orderCode}</strong>
                  </span>
                  {amount != null && (
                    <span>
                      Total: <strong className="font-mono text-foreground">{formatVnd(amount)}</strong>
                    </span>
                  )}
                </div>

                <div
                  id="payos-embedded-container"
                  style={{ minHeight: '350px', width: '100%' }}
                  className="flex items-center justify-center rounded-lg border border-border bg-surface-secondary overflow-hidden"
                />
              </>
            )}
          </Modal.Body>

          <Modal.Footer>
            {!isSuccess && (
              <Button
                variant="secondary"
                isDisabled={isCancelling}
                isPending={isCancelling}
                onPress={() => void handleCancelAndClose()}
                className="w-full sm:w-auto"
              >
                <XCircle className="w-4 h-4 mr-1.5" />
                Cancel / Close
              </Button>
            )}
          </Modal.Footer>
        </Modal.Dialog>
      </Modal.Container>
    </Modal.Backdrop>
  );
}
