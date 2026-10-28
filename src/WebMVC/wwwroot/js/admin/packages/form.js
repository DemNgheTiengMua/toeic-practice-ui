(function() {
    var inputCredits = document.getElementById('pkg-credits');
    var inputPrice = document.getElementById('pkg-price');
    var unitHint = document.getElementById('pkg-unit-hint');

    function updateHint() {
      var credits = parseInt(inputCredits.value, 10) || 1;
      var price = parseFloat(inputPrice.value) || 0;
      var unit = Math.round(price / (credits > 0 ? credits : 1));
      unitHint.textContent = 'Unit price: ' + unit.toLocaleString('en-US') + ' ₫/credit';
    }

    inputCredits.addEventListener('input', updateHint);
    inputPrice.addEventListener('input', updateHint);
  })();
