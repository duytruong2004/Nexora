(() => {
    const region = document.getElementById('nexora-toast-region');
    if (!region) return;

    const buttonTimers = new WeakMap();

    const dismissToast = toast => {
        if (!toast?.isConnected) return;
        toast.classList.remove('is-visible');
        window.setTimeout(() => toast.remove(), 220);
    };

    const showToast = (message, kind = 'success', heading = 'Đã hoàn tất') => {
        while (region.children.length >= 3) region.firstElementChild?.remove();

        const isError = kind === 'error';
        const toast = document.createElement('div');
        toast.className = `nx-toast nx-toast--${kind}`;
        toast.setAttribute('role', isError ? 'alert' : 'status');
        toast.setAttribute('aria-atomic', 'true');

        const icon = document.createElement('span');
        icon.className = 'nx-toast-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = isError ? '!' : '✓';

        const content = document.createElement('div');
        content.className = 'nx-toast-content';
        const title = document.createElement('strong');
        title.textContent = isError ? 'Chưa thể thực hiện' : heading;
        const text = document.createElement('span');
        text.textContent = message;
        content.append(title, text);

        const close = document.createElement('button');
        close.className = 'nx-toast-close';
        close.type = 'button';
        close.setAttribute('aria-label', 'Đóng thông báo');
        close.textContent = '×';
        close.addEventListener('click', () => dismissToast(toast));

        toast.append(icon, content, close);
        region.append(toast);
        window.requestAnimationFrame(() => toast.classList.add('is-visible'));

        if (!isError) window.setTimeout(() => dismissToast(toast), 4200);
    };

    const setBusy = (button, busy, label, spinner, busyText) => {
        button.disabled = busy;
        button.setAttribute('aria-busy', String(busy));
        if (busy) {
            label.textContent = busyText;
            spinner?.classList.remove('d-none');
        } else {
            spinner?.classList.add('d-none');
        }
    };

    const postForm = async form => {
        const body = new URLSearchParams();
        for (const [key, value] of new FormData(form)) body.append(key, String(value));

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
                : 'Máy chủ chưa thể xử lý yêu cầu. Vui lòng thử lại.');
        }

        const result = await response.json();
        if (!response.ok) throw new Error(result.message || 'Yêu cầu chưa thể hoàn tất.');
        return result;
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
        const form = event.target.closest('form[data-catalog-cart-add], form[data-catalog-favorite-add]');
        if (!form) return;

        event.preventDefault();
        const isFavorite = form.matches('[data-catalog-favorite-add]');
        const button = form.querySelector(isFavorite ? '[data-catalog-favorite-button]' : '[data-catalog-cart-add-button]');
        const label = form.querySelector(isFavorite ? '[data-catalog-favorite-label]' : '[data-catalog-cart-add-label]');
        const spinner = form.querySelector(isFavorite ? '[data-catalog-favorite-spinner]' : '[data-catalog-cart-add-spinner]');
        if (!button || !label || button.disabled) return;

        const originalLabel = label.textContent;
        window.clearTimeout(buttonTimers.get(button));
        buttonTimers.delete(button);
        setBusy(button, true, label, spinner, isFavorite ? 'Đang lưu…' : 'Đang thêm…');

        try {
            const result = await postForm(form);
            if (!isFavorite) {
                if (!Number.isInteger(result.itemCount) || result.itemCount < 0) {
                    throw new Error('Số lượng giỏ hàng máy chủ trả về không hợp lệ. Vui lòng tải lại trang.');
                }
                updateCartBadge(result.itemCount);
                showToast(result.message || 'Đã thêm sản phẩm vào giỏ hàng.', 'success', 'Đã thêm vào giỏ');
                label.textContent = 'Đã thêm';
            } else {
                showToast(result.message || 'Đã thêm sản phẩm vào danh sách yêu thích.', 'success', 'Đã lưu yêu thích');
                label.textContent = result.alreadyApplied ? '♥ Đã yêu thích' : '♥ Đã lưu';
                const productName = button.dataset.productName || 'sản phẩm';
                button.setAttribute('aria-label', result.alreadyApplied
                    ? `${productName} đã có trong danh sách yêu thích`
                    : `Đã thêm ${productName} vào danh sách yêu thích`);
                button.classList.remove('btn-outline-secondary');
                button.classList.add('btn-outline-primary', 'favorite-saved');
            }

            spinner?.classList.add('d-none');
            button.setAttribute('aria-busy', 'false');
            button.disabled = isFavorite;
            if (!isFavorite) {
                const timer = window.setTimeout(() => {
                    if (label.isConnected) label.textContent = originalLabel;
                    buttonTimers.delete(button);
                }, 1400);
                buttonTimers.set(button, timer);
            }
        } catch (error) {
            showToast(error instanceof Error ? error.message : 'Không thể hoàn tất yêu cầu. Vui lòng thử lại.', 'error');
            label.textContent = originalLabel;
            button.disabled = false;
            button.setAttribute('aria-busy', 'false');
            spinner?.classList.add('d-none');
        }
    });
})();
