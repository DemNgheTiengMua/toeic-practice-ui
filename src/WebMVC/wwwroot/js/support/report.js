var currentType = document.getElementById("sr-category").dataset.currentType;

  function updateFormCategories() {
    var cat = document.getElementById("sr-category").value;
    var typeSelect = document.getElementById("sr-type");
    typeSelect.innerHTML = "";

    var bookingField = document.getElementById("field-booking");
    var attemptField = document.getElementById("field-attempt");
    var orderField = document.getElementById("field-order");

    bookingField.style.display = "none";
    attemptField.style.display = "none";
    orderField.style.display = "none";

    if (cat === "mentor") {
      bookingField.style.display = "";
      addOption(typeSelect, "mentor_no_show", "The mentor did not show up", currentType);
      addOption(typeSelect, "mentor_quality", "The session quality was poor", currentType);
    } else if (cat === "exam") {
      attemptField.style.display = "";
      addOption(typeSelect, "grading_issue", "An issue with grading or scoring", currentType);
      addOption(typeSelect, "technical_error", "Technical error during exam", currentType);
    } else if (cat === "payment") {
      orderField.style.display = "";
      addOption(typeSelect, "payment_issue", "A payment or credit issue", currentType);
    }
  }

  function addOption(select, value, text, selectedVal) {
    var opt = document.createElement("option");
    opt.value = value;
    opt.textContent = text;
    if (value === selectedVal) opt.selected = true;
    select.appendChild(opt);
  }

  document.addEventListener("DOMContentLoaded", function () {
    var typeSelect = document.getElementById("sr-type");
    if (typeSelect) {
      typeSelect.addEventListener("change", function () {
        currentType = this.value;
      });
    }
    updateFormCategories();
  });
