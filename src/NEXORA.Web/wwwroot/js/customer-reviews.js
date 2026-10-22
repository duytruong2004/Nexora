(() => {
    const page = document.querySelector("[data-reviews-page]");
    if (!page) return;

    const tabs = Array.from(page.querySelectorAll("[data-reviews-tab]"));
    const panels = Array.from(page.querySelectorAll("[data-reviews-panel]"));

    function activateTab(tabName, moveFocus = false) {
        tabs.forEach((tab) => {
            const active = tab.dataset.reviewsTab === tabName;
            tab.setAttribute("aria-selected", String(active));
            tab.tabIndex = active ? 0 : -1;
            tab.classList.toggle("is-active", active);
            if (active && moveFocus) tab.focus();
        });

        panels.forEach((panel) => {
            panel.hidden = panel.dataset.reviewsPanel !== tabName;
        });
    }

    tabs.forEach((tab, index) => {
        tab.addEventListener("click", () => activateTab(tab.dataset.reviewsTab));
        tab.addEventListener("keydown", (event) => {
            let nextIndex = index;
            if (event.key === "ArrowRight") nextIndex = (index + 1) % tabs.length;
            else if (event.key === "ArrowLeft") nextIndex = (index - 1 + tabs.length) % tabs.length;
            else if (event.key === "Home") nextIndex = 0;
            else if (event.key === "End") nextIndex = tabs.length - 1;
            else return;

            event.preventDefault();
            activateTab(tabs[nextIndex].dataset.reviewsTab, true);
        });
    });

    page.querySelectorAll("[data-rating-group]").forEach((group) => {
        const choices = Array.from(group.querySelectorAll("[data-star-choice]"));
        const feedback = group.querySelector("[data-rating-feedback]");
        let selectedRating = Number(group.dataset.selectedRating || 0);

        function paintStars(value, selected) {
            choices.forEach((choice) => {
                const rating = Number(choice.dataset.starChoice);
                choice.classList.toggle("is-preview", !selected && value > 0 && rating <= value);
                choice.classList.toggle("is-selected", selected && value > 0 && rating <= value);
            });
            if (feedback) {
                feedback.textContent = value > 0
                    ? `Bạn đã chọn ${value}/5 sao.`
                    : "Chọn từ 1 đến 5 sao.";
            }
        }

        choices.forEach((choice) => {
            const input = choice.querySelector("input[type=radio]");
            choice.addEventListener("pointerenter", () => paintStars(Number(choice.dataset.starChoice), false));
            input?.addEventListener("focus", () => paintStars(Number(choice.dataset.starChoice), false));
            input?.addEventListener("change", () => {
                selectedRating = Number(input.value);
                paintStars(selectedRating, true);
            });
        });

        group.addEventListener("pointerleave", () => paintStars(selectedRating, selectedRating > 0));
        group.addEventListener("focusout", (event) => {
            if (!group.contains(event.relatedTarget)) paintStars(selectedRating, selectedRating > 0);
        });
        paintStars(selectedRating, selectedRating > 0);
    });

    page.querySelectorAll("[data-open-review-dialog]").forEach((button) => {
        button.addEventListener("click", () => {
            const dialog = document.getElementById(button.dataset.openReviewDialog);
            if (dialog instanceof HTMLDialogElement && !dialog.open) dialog.showModal();
        });
    });

    page.querySelectorAll("[data-review-dialog]").forEach((dialog) => {
        dialog.querySelectorAll("[data-close-review-dialog]").forEach((button) => {
            button.addEventListener("click", () => dialog.close());
        });

        dialog.addEventListener("click", (event) => {
            if (event.target !== dialog) return;
            const bounds = dialog.getBoundingClientRect();
            const outside = event.clientX < bounds.left || event.clientX > bounds.right ||
                event.clientY < bounds.top || event.clientY > bounds.bottom;
            if (outside) dialog.close();
        });
    });

    page.querySelectorAll("[data-review-form]").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (form.dataset.submitting === "true") {
                event.preventDefault();
                return;
            }

            form.dataset.submitting = "true";
            const button = form.querySelector("[data-review-submit]");
            if (button) {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
                const label = button.querySelector("[data-submit-label]");
                if (label) label.textContent = "Đang gửi đánh giá…";
            }
        });
    });

    const reopenOrderDetailId = Number(page.dataset.reopenReviewId || 0);
    if (reopenOrderDetailId > 0) {
        const dialog = page.querySelector(`[data-review-dialog][data-order-detail-id="${reopenOrderDetailId}"]`);
        if (dialog instanceof HTMLDialogElement) {
            activateTab("eligible");
            dialog.showModal();
            dialog.querySelector("[aria-invalid='true'], input[type=radio]")?.focus();
        }
    }
})();
