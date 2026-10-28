document.addEventListener("DOMContentLoaded", function () {
      var form = document.getElementById("checkout-form");
      var embeddedModal = document.getElementById("embedded-payment-modal");
      var btnClose = document.getElementById("btn-close-embedded");
      var directLink = document.getElementById("btn-direct-checkout");
      var modalCancelOrderCode = document.getElementById("modal-cancel-orderCode");
      var btnResume = document.getElementById("btn-resume-qr");
      var payosInstance = null;

      function closeModal() {
        if (payosInstance) {
          try { payosInstance.exit(); } catch (e) {}
        }
        var container = document.getElementById("embedded-payment-container");
        if (container) container.innerHTML = "";
        if (embeddedModal) embeddedModal.style.display = "none";
        var submitBtn = form ? form.querySelector("button[type='submit']") : null;
        if (submitBtn) submitBtn.disabled = false;
      }

      function openQrModal(order) {
        var codeEl = document.getElementById("modal-order-code");
        var pkgEl = document.getElementById("modal-package-name");
        var amtEl = document.getElementById("modal-amount");
        var credEl = document.getElementById("modal-credits");

        if (codeEl) codeEl.textContent = order.orderCode || "";
        if (pkgEl) pkgEl.textContent = order.packageName || "Credit Package";
        if (amtEl) amtEl.textContent = order.amount || "";
        if (credEl) credEl.textContent = "+" + (order.credits || "0") + " credits";
        if (modalCancelOrderCode) modalCancelOrderCode.value = order.orderCode || "";
        if (directLink) directLink.href = order.checkoutUrl || "#";

        if (window.PayOSCheckout && typeof window.PayOSCheckout.usePayOS === "function" && order.checkoutUrl) {
          var container = document.getElementById("embedded-payment-container");
          if (container) container.innerHTML = "";
          if (embeddedModal) embeddedModal.style.display = "flex";

          var config = {
            RETURN_URL: window.location.origin + "/payment/result?orderCode=" + encodeURIComponent(order.orderCode),
            ELEMENT_ID: "embedded-payment-container",
            CHECKOUT_URL: order.checkoutUrl,
            embedded: true,
            onSuccess: function (event) {
              window.location.href = "/payment/result?orderCode=" + encodeURIComponent(order.orderCode);
            },
            onCancel: function (event) {
              console.log("payOS onCancel:", event);
            },
            onExit: function (event) {
              console.log("payOS onExit:", event);
              closeModal();
            }
          };

          try {
            payosInstance = PayOSCheckout.usePayOS(config);
            payosInstance.open();
          } catch (popupErr) {
            console.warn("PayOS embedded open failed, redirecting to hosted checkout:", popupErr);
            window.location.href = order.checkoutUrl;
          }
        } else if (order.checkoutUrl) {
          window.location.href = order.checkoutUrl;
        }
      }

      if (btnClose) {
        btnClose.addEventListener("click", closeModal);
      }

      if (btnResume) {
        btnResume.addEventListener("click", function () {
          openQrModal({
            orderCode: btnResume.getAttribute("data-ordercode"),
            packageName: btnResume.getAttribute("data-packagename"),
            amount: btnResume.getAttribute("data-amount"),
            credits: btnResume.getAttribute("data-credits"),
            checkoutUrl: btnResume.getAttribute("data-checkouturl")
          });
        });

        // If URL has ?openQr=1, auto-trigger the resume modal
        var urlParams = new URLSearchParams(window.location.search);
        if (urlParams.get("openQr") === "1") {
          btnResume.click();
        }
      }

      if (form) {
        form.addEventListener("submit", async function (e) {
          e.preventDefault();
          var submitBtn = form.querySelector("button[type='submit']");
          if (submitBtn) submitBtn.disabled = true;

          try {
            var tokenInput = form.querySelector("input[name='__RequestVerificationToken']");
            var packageIdInput = form.querySelector("input[name='packageId']");
            var formData = new FormData();
            formData.append("packageId", packageIdInput ? packageIdInput.value : "");
            if (tokenInput) formData.append("__RequestVerificationToken", tokenInput.value);

            var res = await fetch("/checkout", {
              method: "POST",
              headers: {
                "Accept": "application/json",
                "X-Requested-With": "XMLHttpRequest"
              },
              body: formData
            });

            if (!res.ok) {
              var errData = null;
              try { errData = await res.json(); } catch (_) {}
              if (errData && errData.message) {
                alert(errData.message);
              } else {
                alert("Could not create payment order. Please refresh and try again.");
              }
              if (submitBtn) submitBtn.disabled = false;
              return;
            }

            var data = await res.json();
            if (data.gateway === "payos" && data.checkoutUrl) {
              openQrModal({
                orderCode: data.orderCode,
                packageName: form.dataset.packageName,
                amount: form.dataset.amount,
                credits: form.dataset.credits,
                checkoutUrl: data.checkoutUrl
              });
            } else if (data.checkoutUrl) {
              window.location.href = data.checkoutUrl;
            } else {
              if (submitBtn) submitBtn.disabled = false;
            }
          } catch (err) {
            console.error("Checkout submission failed", err);
            alert("An unexpected error occurred. Please try again.");
            if (submitBtn) submitBtn.disabled = false;
          }
        });
      }
    });
