/**
 * Keyboard navigation enhancements for TOEIC Practice exam interface.
 * Handles modal focus traps, Escape key, initial focus, and focus return.
 */
(function () {
  "use strict";

  var lastFocusedElement = null;
  var activeModal = null;

  /**
   * Get all focusable elements within a container
   */
  function getFocusableElements(container) {
    var selector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    return Array.prototype.slice.call(container.querySelectorAll(selector));
  }

  /**
   * Trap focus within modal - prevent Tab from escaping
   */
  function trapFocus(modal, event) {
    var focusableElements = getFocusableElements(modal);
    if (focusableElements.length === 0) return;

    var firstElement = focusableElements[0];
    var lastElement = focusableElements[focusableElements.length - 1];

    // Tab forward from last element -> focus first
    if (event.key === 'Tab' && !event.shiftKey && document.activeElement === lastElement) {
      event.preventDefault();
      firstElement.focus();
    }

    // Shift+Tab backward from first element -> focus last
    if (event.key === 'Tab' && event.shiftKey && document.activeElement === firstElement) {
      event.preventDefault();
      lastElement.focus();
    }
  }

  /**
   * Close modal on Escape key
   */
  function handleEscape(event) {
    if (event.key === 'Escape' && activeModal) {
      // For prototype: log the escape attempt
      console.info('[keyboard-nav] Escape pressed - in production this would close the modal and return focus');

      // In a real app, this would:
      // 1. Hide the modal
      // 2. Return focus to lastFocusedElement
      // 3. Set activeModal = null

      // For the prototype, we can't actually close modals (they're state-switched),
      // but we document the expected behavior
    }
  }

  /**
   * Initialize modal when it becomes visible
   */
  function initModal(modal) {
    if (!modal || activeModal === modal) return;

    // Store the element that had focus before modal opened
    lastFocusedElement = document.activeElement;
    activeModal = modal;

    // Move focus to first focusable element in modal
    var focusableElements = getFocusableElements(modal);
    if (focusableElements.length > 0) {
      // Prefer focusing the primary action button, or first focusable element
      var primaryButton = modal.querySelector('.btn--primary');
      var elementToFocus = primaryButton || focusableElements[0];

      // Small delay to ensure modal is fully rendered
      setTimeout(function() {
        elementToFocus.focus();
      }, 100);
    }

    console.info('[keyboard-nav] Modal initialized with focus trap');
  }

  /**
   * Set up keyboard navigation on page load
   */
  function init() {
    // Handle Escape key globally
    document.addEventListener('keydown', handleEscape);

    // Find visible modals and initialize them
    var modals = document.querySelectorAll('.modal-overlay');
    modals.forEach(function(overlay) {
      // Check if modal is visible (using the state system)
      var computedStyle = window.getComputedStyle(overlay);
      if (computedStyle.display !== 'none') {
        var modal = overlay.querySelector('.modal');
        if (modal) {
          initModal(modal);
        }
      }

      // Set up focus trap for this modal's overlay
      overlay.addEventListener('keydown', function(event) {
        var modal = overlay.querySelector('.modal');
        if (modal && window.getComputedStyle(overlay).display !== 'none') {
          trapFocus(modal, event);
        }
      });
    });

    // Add keyboard navigation to question navigator cells
    setupNavigatorKeyboard();

    // Add keyboard shortcuts for common exam actions
    setupExamKeyboardShortcuts();

    console.info('[keyboard-nav] Keyboard navigation initialized');
  }

  /**
   * Arrow key navigation for question navigator grid
   */
  function setupNavigatorKeyboard() {
    var navigatorCells = document.querySelectorAll('.navigator__cell');
    if (navigatorCells.length === 0) return;

    navigatorCells.forEach(function(cell, index) {
      cell.addEventListener('keydown', function(event) {
        var cols = 10; // Navigator grid is 10 columns
        var currentIndex = index;
        var targetIndex = -1;

        switch(event.key) {
          case 'ArrowRight':
            targetIndex = currentIndex + 1;
            break;
          case 'ArrowLeft':
            targetIndex = currentIndex - 1;
            break;
          case 'ArrowDown':
            targetIndex = currentIndex + cols;
            break;
          case 'ArrowUp':
            targetIndex = currentIndex - cols;
            break;
          case 'Home':
            targetIndex = 0;
            break;
          case 'End':
            targetIndex = navigatorCells.length - 1;
            break;
          default:
            return; // Don't prevent default for other keys
        }

        if (targetIndex >= 0 && targetIndex < navigatorCells.length) {
          event.preventDefault();
          navigatorCells[targetIndex].focus();
        }
      });
    });

    console.info('[keyboard-nav] Navigator arrow key navigation enabled');
  }

  /**
   * Keyboard shortcuts for exam actions
   */
  function setupExamKeyboardShortcuts() {
    document.addEventListener('keydown', function(event) {
      // Skip if user is typing in an input/textarea
      var activeElement = document.activeElement;
      if (activeElement.tagName === 'INPUT' ||
          activeElement.tagName === 'TEXTAREA' ||
          activeElement.tagName === 'SELECT') {
        return;
      }

      // Skip if modal is open (modal has its own keyboard handling)
      if (activeModal) return;

      // F - Flag for review (when not in a modal)
      if (event.key === 'f' || event.key === 'F') {
        var flagButton = document.querySelector('.btn--ghost[aria-pressed]');
        if (flagButton) {
          event.preventDefault();
          flagButton.click();
          console.info('[keyboard-nav] Flag toggled via keyboard shortcut');
        }
      }

      // N - Next question
      if (event.key === 'n' || event.key === 'N') {
        var nextButton = document.querySelector('.btn--primary[aria-label*="next" i]');
        if (nextButton) {
          event.preventDefault();
          nextButton.click();
        }
      }

      // P - Previous question
      if (event.key === 'p' || event.key === 'P') {
        var prevButton = document.querySelector('.btn[aria-label*="previous" i]');
        if (prevButton && !prevButton.disabled) {
          event.preventDefault();
          prevButton.click();
        }
      }

      // 1-4 - Select answer option (A-D)
      if (event.key >= '1' && event.key <= '4') {
        var optionIndex = parseInt(event.key) - 1;
        var options = document.querySelectorAll('.option__input');
        if (options[optionIndex]) {
          event.preventDefault();
          options[optionIndex].checked = true;
          options[optionIndex].focus();
          console.info('[keyboard-nav] Answer option ' + String.fromCharCode(65 + optionIndex) + ' selected');
        }
      }
    });

    console.info('[keyboard-nav] Exam keyboard shortcuts enabled (F=flag, N=next, P=prev, 1-4=answers)');
  }

  // Initialize when DOM is ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
