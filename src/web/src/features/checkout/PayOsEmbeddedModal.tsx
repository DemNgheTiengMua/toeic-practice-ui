import React, { useEffect, useRef, useState } from 'react';
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
  onClose: () => void;
  onSuccess?: () => void;
}

export function PayOsEmbeddedModal({
  isOpen,
  orderCode,
  checkoutUrl,
  amount,
  credits,
  onClose,
  onSuccess,
}: PayOsEmbeddedModalProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [isSuccess, setIsSuccess] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  // payOS fires onExit during teardown as well as on a real cancel; without this flag the cleanup
  // that follows our own navigation cancels the order and redirects again.
  const hasLeft = useRef(false);

  // #381: both paying and cancelling land on the receipt for this order. The checkout page's
  // returnUrl sent a paid learner to /pricing, where nothing said the order had settled.
  // The receipt reads ?orderCode=, so one helper serves both destinations.
  const receipt = { to: '/payment/result', search: { orderCode } } as any;

  const handleCancelAndClose = async () => {
    if (hasLeft.current) return;
    hasLeft.current = true;
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
      void navigate(receipt);
    }
  };

  // payOS compares this frame's redirect_uri against the returnUrl the link was created with, and
  // refuses the request when they differ. window.location.href was the checkout page itself, so every
  // embed answered "Thông tin truyền lên không hợp lệ". The result page is the URL the link was
  // registered with, so the frame accepts it.
  const payOsReturnUrl =
    typeof window !== 'undefined'
      ? `${window.location.origin}/payment/result?orderCode=${encodeURIComponent(orderCode)}`
      : '';

  const { open, exit } = usePayOS({
    RETURN_URL: payOsReturnUrl,
    ELEMENT_ID: 'payos-embedded-container',
    CHECKOUT_URL: checkoutUrl,
    embedded: true,
    onSuccess: (_event) => {
      setIsSuccess(true);
      // A paid order must never be cancelled by the teardown onExit that follows our navigation.
      hasLeft.current = true;
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
        void navigate(receipt);
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
        <Modal.Dialog className="sm:max-w-2xl w-full">
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

                {/*
                  payOS renders a full payment panel (QR + bank details + instructions) and sizes its
                  own document. A fixed minHeight with overflow-hidden clipped it and produced two
                  nested scrollbars. Giving the iframe a tall, scroll-free area lets the frame draw at
                  its own height, and the class list stays minimal so nothing competes with payOS's
                  own layout.
                */}
                <div
                  id="payos-embedded-container"
                  style={{ height: '560px', width: '100%' }}
                  className="overflow-hidden rounded-lg border border-border"
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
