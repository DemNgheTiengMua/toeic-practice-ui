(function() {
    var modal = document.getElementById('pkg-modal');
    var form = document.getElementById('pkg-modal-form');
    var modalTitle = document.getElementById('modal-pkg-title');
    var btnCreate = document.getElementById('btn-create-pkg');
    var btnCancel = document.getElementById('modal-pkg-cancel');
    var inputName = document.getElementById('modal-pkg-name');
    var inputCredits = document.getElementById('modal-pkg-credits');
    var inputPrice = document.getElementById('modal-pkg-price');
    var inputStatus = document.getElementById('modal-pkg-status');
    var inputOrder = document.getElementById('modal-pkg-order');
    var inputFeatures = document.getElementById('modal-pkg-features');
    var inputFeatured = document.getElementById('modal-pkg-featured');
    var unitHint = document.getElementById('modal-pkg-unit-hint');

    function updateHint() {
      var credits = parseInt(inputCredits.value, 10) || 1;
      var price = parseFloat(inputPrice.value) || 0;
      var unit = Math.round(price / (credits > 0 ? credits : 1));
      unitHint.textContent = 'Unit price: ' + unit.toLocaleString('en-US') + ' ₫/credit';
    }

    inputCredits.addEventListener('input', updateHint);
    inputPrice.addEventListener('input', updateHint);

    function openCreate(e) {
      if (e) e.preventDefault();
      modalTitle.textContent = 'Create new package';
      form.action = '/admin/packages/create';
      inputName.value = '';
      inputCredits.value = '5';
      inputPrice.value = '199000';
      inputStatus.value = 'true';
      inputOrder.value = '1';
      inputFeatures.value = '';
      inputFeatured.checked = false;
      updateHint();
      modal.style.display = 'flex';
      inputName.focus();
    }

    function openEdit(e, btn) {
      if (e) e.preventDefault();
      var id = btn.getAttribute('data-id');
      modalTitle.textContent = 'Edit package — ' + btn.getAttribute('data-name');
      form.action = '/admin/packages/edit/' + id;
      inputName.value = btn.getAttribute('data-name') || '';
      inputCredits.value = btn.getAttribute('data-credits') || '1';
      inputPrice.value = btn.getAttribute('data-price') || '0';
      inputStatus.value = btn.getAttribute('data-active') === 'true' ? 'true' : 'false';
      inputOrder.value = btn.getAttribute('data-order') || '0';
      inputFeatures.value = btn.getAttribute('data-features') || '';
      inputFeatured.checked = btn.getAttribute('data-featured') === 'true';
      updateHint();
      modal.style.display = 'flex';
      inputName.focus();
    }

    function closeModal() {
      modal.style.display = 'none';
    }

    if (btnCreate) {
      btnCreate.addEventListener('click', openCreate);
    }
    if (btnCancel) {
      btnCancel.addEventListener('click', closeModal);
    }

    var editBtns = document.querySelectorAll('.btn-edit-pkg');
    editBtns.forEach(function(btn) {
      btn.addEventListener('click', function(e) {
        openEdit(e, btn);
      });
    });

    modal.addEventListener('click', function(e) {
      if (e.target === modal) {
        closeModal();
      }
    });
  })();
