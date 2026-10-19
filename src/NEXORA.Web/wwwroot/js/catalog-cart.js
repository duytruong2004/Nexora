(() => {
    const feedback = document.getElementById('catalog-cart-feedback');
    if (!feedback) return;

    let feedbackTimer;
    const buttonTimers = new WeakMap();

    const showFeedback = (message, isError) => {
        window.clearTimeout(feedbackTimer);
        feedback.textContent = message;
        feedback.classList.remove('d-none', 'alert-success', 'alert-danger');
        feedback.classList.add(isError ? 'alert-danger' : 'alert-success');
        feedback.setAttribute('role', isError ? 'alert' : 'status');
        feedback.setAttribute('aria-live', isError ? 'assertive' : 'polite');

        if (!isError) {
            feedbackTimer = window.setTimeout(() => feedback.classList.add('d-none'), 2800);
        }
    };

    const updateCartBadge = itemCount => {
        document.querySelectorAll('[data-cart-summary-link]').forEach(link => {
            link.setAttribute('aria-label', `Giỏ hàng, ${itemCount} sản phẩm`);
        });

        document.querySelectorAll('[data-cart-count]').forEach(badge => {
            badge.textContent = itemCount > 99 ? '99+' : String(itemCount);
            badge.classList.toggle('d-none', itemCount <= 0);
        });
    };

    document.addEventListener('submit', async event => {
        const form = event.target.closest('form[data-catalog-cart-add]');
        if (!form) return;

        event.preventDefault();
        const button = form.querySelector('[data-catalog-cart-add-button]');
        if (!button || button.disabled) return;

        const label = button.querySelector('[data-catalog-cart-add-label]');
        const spinner = button.querySelector('[data-catalog-cart-add-spinner]');
        const originalLabel = label.textContent;
        window.clearTimeout(buttonTimers.get(button));
        buttonTimers.delete(button);
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        label.textContent = 'Đang thêm…';
        spinner.classList.remove('d-none');
        feedback.classList.add('d-none');

        try {
            const body = new URLSearchParams();
            for (const [key, value] of new FormData(form)) {
                body.append(key, String(value));
            }

            const response = await fetch(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body
            });

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.toLowerCase().includes('application/json')) {
                throw new Error(response.redirected
                    ? 'Phiên đăng nhập có thể đã hết hạn. Hãy đăng nhập lại rồi thử tiếp.'
                    : 'Không thể thêm sản phẩm vào giỏ hàng. Hãy thử lại.');
            }

            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Không thể thêm sản phẩm vào giỏ hàng.');
            if (!Number.isInteger(result.itemCount) || result.itemCount < 0) {
                throw new Error('Máy chủ trả về số lượng giỏ hàng không hợp lệ. Hãy tải lại trang.');
            }

            updateCartBadge(result.itemCount);
            showFeedback(result.message || 'Đã thêm vào giỏ hàng.', false);
            label.textContent = 'Đã thêm';
        } catch (error) {
            showFeedback(error instanceof Error ? error.message : 'Không thể thêm sản phẩm vào giỏ hàng. Hãy thử lại.', true);
            label.textContent = originalLabel;
        } finally {
            spinner.classList.add('d-none');
            button.setAttribute('aria-busy', 'false');
            button.disabled = false;
            if (label.textContent === 'Đã thêm') {
                const timer = window.setTimeout(() => {
                    if (label.isConnected) label.textContent = originalLabel;
                    buttonTimers.delete(button);
                }, 1400);
                buttonTimers.set(button, timer);
            }
        }
    });
})();
