function confirmDeleteLesson() {
    if (confirm('Are you sure you want to delete lesson "' + document.getElementById('delete-lesson-form').dataset.lessonTitle + '"? This will permanently remove the lesson and all walkthrough cards. This action cannot be undone.')) {
      document.getElementById('delete-lesson-form').submit();
    }
  }

  function onCertChanged(certId) {
    certId = String(certId);
    // Filter skill options
    const skillSelect = document.getElementById('SkillId');
    if (skillSelect) {
      let firstMatch = null;
      Array.from(skillSelect.options).forEach(opt => {
        const match = opt.getAttribute('data-cert') === certId;
        opt.hidden = !match;
        if (match && !firstMatch) firstMatch = opt;
      });
      if (firstMatch && (!skillSelect.selectedOptions[0] || skillSelect.selectedOptions[0].hidden)) {
        skillSelect.value = firstMatch.value;
      }
    }

    // Filter category options
    const catSelect = document.getElementById('CategoryId');
    if (catSelect) {
      Array.from(catSelect.options).forEach(opt => {
        if (!opt.value) { opt.hidden = false; return; }
        opt.hidden = opt.getAttribute('data-cert') !== certId;
      });
      if (catSelect.selectedOptions[0] && catSelect.selectedOptions[0].hidden) {
        catSelect.value = '';
      }
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    const certEl = document.getElementById('CertId');
    if (certEl && certEl.value) {
      onCertChanged(certEl.value);
    }
  });

  function reindexCards() {
    const container = document.getElementById('cards-container');
    const cards = container.querySelectorAll('.card-item');

    cards.forEach((card, idx) => {
      card.setAttribute('data-index', idx);
      card.querySelector('.card-num').textContent = (idx + 1);
      card.querySelector('.card-order').value = idx;

      // Update all input names: Cards[old].Prop -> Cards[idx].Prop
      const inputs = card.querySelectorAll('input, select, textarea');
      inputs.forEach(input => {
        const name = input.getAttribute('name');
        if (name && name.startsWith('Cards[')) {
          const newName = name.replace(/Cards\[\d+\]/, 'Cards[' + idx + ']');
          input.setAttribute('name', newName);
        }
      });
    });
  }

  function addCard(type) {
    const container = document.getElementById('cards-container');
    const idx = container.querySelectorAll('.card-item').length;
    const template = document.getElementById('card-template').innerHTML;

    let defaultTitle = 'New ' + type.charAt(0).toUpperCase() + type.slice(1);
    let defaultContent = 'Write ' + type + ' content here.';

    if (type === 'micro_check') {
      defaultTitle = 'Check Your Understanding';
      defaultContent = 'Answer the quick check question below.';
    }

    let html = template
      .replaceAll('__INDEX__', idx)
      .replaceAll('__NUM__', idx + 1)
      .replaceAll('__TYPE_UPPER__', type.toUpperCase())
      .replaceAll('__TITLE__', defaultTitle)
      .replaceAll('__CONTENT__', defaultContent);

    const tempDiv = document.createElement('div');
    tempDiv.innerHTML = html;
    const newCard = tempDiv.firstElementChild;

    const select = newCard.querySelector('.card-type-select');
    select.value = type;

    if (type === 'micro_check') {
      newCard.querySelector('.micro-check-fields').style.display = 'block';
    }

    container.appendChild(newCard);
    reindexCards();
    newCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  function removeCard(btn) {
    const container = document.getElementById('cards-container');
    if (container.querySelectorAll('.card-item').length <= 1) {
      alert('A lesson deck must contain at least one card.');
      return;
    }
    const card = btn.closest('.card-item');
    card.remove();
    reindexCards();
  }

  function moveCardUp(btn) {
    const card = btn.closest('.card-item');
    const prev = card.previousElementSibling;
    if (prev) {
      card.parentNode.insertBefore(card, prev);
      reindexCards();
    }
  }

  function moveCardDown(btn) {
    const card = btn.closest('.card-item');
    const next = card.nextElementSibling;
    if (next) {
      card.parentNode.insertBefore(next, card);
      reindexCards();
    }
  }

  function onCardTypeChanged(select) {
    const card = select.closest('.card-item');
    const type = select.value;
    card.querySelector('.card-type-label').textContent = type.toUpperCase();

    const microCheckFields = card.querySelector('.micro-check-fields');
    if (type === 'micro_check') {
      microCheckFields.style.display = 'block';
    } else {
      microCheckFields.style.display = 'none';
    }
  }
