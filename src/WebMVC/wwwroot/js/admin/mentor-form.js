(function () {
  var typeSelect = document.getElementById('mentor-type');
  var userGroup = document.getElementById('user-field-group');
  var userSelect = document.getElementById('mentor-user');
  var certSelect = document.getElementById('mentor-cert');
  var skillSelect = document.getElementById('mentor-skill');

  function updateVisibility() {
    if (!typeSelect) return;
    var isAi = typeSelect.value === 'ai';
    if (isAi) {
      userGroup.style.display = 'none';
      if (userSelect) userSelect.value = '';
    } else {
      userGroup.style.display = '';
    }
  }

  function updateSkills() {
    if (!certSelect || !skillSelect) return;
    var selectedCert = certSelect.value;
    for (var i = 0; i < skillSelect.options.length; i++) {
      var opt = skillSelect.options[i];
      var optCert = opt.getAttribute('data-cert');
      if (!optCert) continue;
      if (optCert === selectedCert) {
        opt.style.display = '';
        opt.disabled = false;
      } else {
        opt.style.display = 'none';
        opt.disabled = true;
        if (opt.selected) skillSelect.value = '';
      }
    }
  }

  if (typeSelect) {
    typeSelect.addEventListener('change', updateVisibility);
    updateVisibility();
  }

  if (certSelect) {
    certSelect.addEventListener('change', updateSkills);
    updateSkills();
  }
})();