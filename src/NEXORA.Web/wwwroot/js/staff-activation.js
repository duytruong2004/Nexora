(() => {
    const form = document.getElementById("staff-activation-form");
    const status = document.getElementById("invitation-status");
    const invalid = document.getElementById("invitation-invalid");

    if (!form || !status || !invalid) return;

    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const emailInput = form.elements.namedItem("Email");
    const tokenInput = form.elements.namedItem("Token");
    const fromFragment = !emailInput.value || !tokenInput.value;
    const email = emailInput.value || fragment.get("email") || "";
    const token = tokenInput.value || fragment.get("token") || "";

    // Invitation secrets stay in the URL fragment, which browsers never send in HTTP requests.
    if (fromFragment) {
        window.history.replaceState(null, document.title, window.location.pathname + window.location.search);
    }

    const showInvalid = () => {
        status.hidden = true;
        invalid.hidden = false;
    };

    if (!email || !token || email.length > 256 || token.length > 4096) {
        showInvalid();
        return;
    }

    emailInput.value = email;
    tokenInput.value = token;

    const antiforgeryToken = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (!antiforgeryToken) {
        showInvalid();
        return;
    }

    const body = new URLSearchParams({ email, token });
    fetch("/account/ValidateStaffInvitation", {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
            "RequestVerificationToken": antiforgeryToken,
            "Accept": "application/json"
        },
        body
    })
        .then(response => response.ok ? response.json() : Promise.reject(new Error("validation_failed")))
        .then(result => {
            if (!result.valid) {
                showInvalid();
                return;
            }

            status.textContent = `Lời mời dành cho ${email}. Hãy tạo mật khẩu để kích hoạt tài khoản.`;
            form.hidden = false;
            form.querySelector("#Password")?.focus();
        })
        .catch(showInvalid);
})();
