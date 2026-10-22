(() => {
    const content = document.getElementById('cart-page-content');
    if (!content) return;

    const message = document.getElementById('cart-ajax-message');
    const emptyTemplate = document.getElementById('cart-empty-template');
    const quantityStates = new WeakMap();
    const numberFormat = new Intl.NumberFormat(document.documentElement.lang || 'vi-VN', {
        maximumFractionDigits: 0
    });

    const formatMoney = amount => numberFormat.format(Number(amount) || 0) + ' ₫';

    const showMessage = (text, isError) => {
        message.textContent = text;
        message.classList.remove('d-none', 'alert-success', 'alert-danger');
        message.classList.add(isError ? 'alert-danger' : 'alert-success');
        message.setAttribute('role', isError ? 'alert' : 'status');
    };

    const clearMessage = () => {
        message.textContent = '';
        message.classList.add('d-none');
        message.classList.remove('alert-success', 'alert-danger');
    };

    const setQuantityStatus = (form, text, isError = false) => {
        const status = form.querySelector('[data-cart-quantity-status]');
        if (!status) return;
        status.textContent = text;
        status.classList.toggle('text-danger', isError);
        status.classList.toggle('text-secondary', !isError);
    };

    const getQuantityState = form => {
        let state = quantityStates.get(form);
        if (state) return state;

        const input = form.querySelector('[data-cart-quantity]');
        const initial = Number(input.value) || 1;
        state = {
            confirmedQuantity: initial,
            desiredQuantity: initial,
            version: 0,
            timer: null,
            inFlight: false,
            invalid: false
        };
        quantityStates.set(form, state);
        return state;
    };

    const readQuantity = input => {
        const value = input.value.trim();
        if (!/^[0-9]+$/.test(value)) return null;
        const quantity = Number(value);
        return Number.isSafeInteger(quantity) && quantity > 0 ? quantity : null;
    };

    const updateCartBadge = itemCount => {
        document.querySelectorAll('[data-cart-summary-link]').forEach(link => {
            link.setAttribute('aria-label', 'Giỏ hàng, ' + itemCount + ' sản phẩm');
        });

        document.querySelectorAll('[data-cart-count]').forEach(badge => {
            badge.textContent = itemCount > 99 ? '99+' : String(itemCount);
            badge.classList.toggle('d-none', itemCount <= 0);
        });
    };

    const updateRow = (item, preserveQuantity = false) => {
        const row = content.querySelector('[data-cart-item-id="' + item.cartItemId + '"]');
        if (!row) return;

        row.querySelector('[data-cart-unit-price]').textContent = formatMoney(item.unitPrice);
        row.querySelector('[data-cart-line-total]').textContent = formatMoney(item.lineTotal);

        const promotion = row.querySelector('[data-cart-promotion]');
        promotion.classList.toggle('d-none', item.discountAmount <= 0);
        row.querySelector('[data-cart-promotion-name]').textContent = item.promotionName || 'Khuyến mại';
        row.querySelector('[data-cart-discount]').textContent = numberFormat.format(Number(item.discountAmount) || 0);

        const form = row.querySelector('.cart-quantity-form');
        const state = form ? getQuantityState(form) : null;
        const quantity = row.querySelector('[data-cart-quantity]');
        if (!preserveQuantity && !state?.inFlight && !state?.timer && !state?.invalid) {
            quantity.value = item.quantity;
            if (state) {
                state.confirmedQuantity = item.quantity;
                state.desiredQuantity = item.quantity;
            }
        }

        const stockMessage = row.querySelector('[data-cart-stock-message]');
        const stockChanged = !item.isAvailable || item.quantity > item.availableQuantity;
        stockMessage.classList.toggle('d-none', !stockChanged);
        row.querySelector('[data-cart-available]').textContent = item.availableQuantity;
        row.dataset.cartProductAvailable = String(item.isAvailable);

        const controlsDisabled = !item.isAvailable || Boolean(state?.inFlight);
        row.querySelectorAll('[data-cart-quantity-decrease], [data-cart-quantity-increase]')
            .forEach(button => { button.disabled = controlsDisabled; });
        quantity.disabled = !item.isAvailable;
        const removeButton = row.querySelector('[data-cart-remove-button]');
        if (removeButton && !row.dataset.cartRemoving) {
            removeButton.disabled = Boolean(state?.inFlight);
        }
    };

    const updateCart = (cart, options = {}) => {
        updateCartBadge(cart.itemCount);
        const distinctCount = document.querySelector('[data-cart-distinct-count]');
        const totalCount = document.querySelector('[data-cart-total-count]');
        if (distinctCount) distinctCount.textContent = cart.items.length;
        if (totalCount) totalCount.textContent = cart.itemCount;

        if (cart.isEmpty) {
            content.replaceChildren(emptyTemplate.content.cloneNode(true));
            return true;
        }

        cart.items.forEach(item => {
            const preserveQuantity = String(item.cartItemId) === String(options.preserveQuantityFor);
            updateRow(item, preserveQuantity);
        });

        const itemIds = new Set(cart.items.map(item => String(item.cartItemId)));
        content.querySelectorAll('[data-cart-item-id]').forEach(row => {
            if (!itemIds.has(row.dataset.cartItemId)) row.remove();
        });

        content.querySelector('[data-cart-item-count]').textContent = cart.itemCount;
        content.querySelector('[data-cart-subtotal]').textContent = formatMoney(cart.subtotal);
        content.querySelector('[data-cart-promotion-discount]').textContent = numberFormat.format(Number(cart.promotionDiscount) || 0);
        content.querySelector('[data-cart-total]').textContent = formatMoney(cart.total);
        content.querySelector('[data-cart-checkout-warning]').classList.toggle('d-none', cart.canCheckout);
        content.querySelector('[data-cart-checkout-link]').classList.toggle('d-none', !cart.canCheckout);
        content.querySelector('[data-cart-checkout-disabled]').classList.toggle('d-none', cart.canCheckout);

        return false;
    };

    const getSnapshotLine = (cart, cartItemId) =>
        cart?.items?.find(item => String(item.cartItemId) === String(cartItemId));

    const setQuantityBusy = (form, busy) => {
        const row = form.closest('[data-cart-item-id]');
        const input = form.querySelector('[data-cart-quantity]');
        const state = getQuantityState(form);
        const unavailable = input.disabled;
        form.setAttribute('aria-busy', String(busy));
        row.querySelectorAll('[data-cart-quantity-decrease], [data-cart-quantity-increase]')
            .forEach(button => { button.disabled = busy || unavailable; });
        const removeButton = row.querySelector('[data-cart-remove-button]');
        if (removeButton && !row.dataset.cartRemoving) removeButton.disabled = busy;
        if (busy) {
            setQuantityStatus(form, 'Đang cập nhật…');
        } else if (state.timer) {
            setQuantityStatus(form, 'Sẽ cập nhật số lượng mới…');
        }
    };

    const postForm = async (form, quantityOverride) => {
        const body = new URLSearchParams();
        for (const [key, value] of new FormData(form)) body.append(key, String(value));
        if (quantityOverride !== undefined) body.set('quantity', String(quantityOverride));

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
                : 'Không thể cập nhật giỏ hàng. Hãy tải lại trang rồi thử lại.');
        }

        return { response, result: await response.json() };
    };

    const updateQuantity = async form => {
        const input = form.querySelector('[data-cart-quantity]');
        const state = getQuantityState(form);
        if (state.inFlight || input.disabled) return;

        const quantity = readQuantity(input);
        if (quantity === null) {
            state.invalid = true;
            input.setAttribute('aria-invalid', 'true');
            setQuantityStatus(form, 'Nhập số nguyên lớn hơn hoặc bằng 1.', true);
            return;
        }

        state.invalid = false;
        input.removeAttribute('aria-invalid');
        state.desiredQuantity = quantity;
        if (quantity === state.confirmedQuantity) {
            setQuantityStatus(form, '');
            return;
        }

        const requestVersion = state.version;
        const row = form.closest('[data-cart-item-id]');
        const cartItemId = row.dataset.cartItemId;
        state.inFlight = true;
        clearMessage();
        setQuantityBusy(form, true);

        try {
            const { response, result } = await postForm(form, quantity);
            const hasNewerInput = state.version !== requestVersion;
            const nextQuantity = readQuantity(input);
            const hasPendingQuantity = hasNewerInput &&
                nextQuantity !== null &&
                nextQuantity !== quantity;

            if (!response.ok) {
                if (hasPendingQuantity && result.cart) {
                    const serverLine = getSnapshotLine(result.cart, cartItemId);
                    if (serverLine) state.confirmedQuantity = serverLine.quantity;
                    state.inFlight = false;
                    updateCart(result.cart, { preserveQuantityFor: cartItemId });
                    setQuantityBusy(form, false);
                    if (!state.timer) void updateQuantity(form);
                    return;
                }

                state.inFlight = false;
                if (result.cart) {
                    updateCart(result.cart);
                    const serverLine = getSnapshotLine(result.cart, cartItemId);
                    if (serverLine) {
                        state.confirmedQuantity = serverLine.quantity;
                        state.desiredQuantity = serverLine.quantity;
                    }
                } else {
                    input.value = String(state.confirmedQuantity);
                }
                setQuantityBusy(form, false);
                const errorMessage = result.message || 'Không thể cập nhật số lượng.';
                setQuantityStatus(form, errorMessage, true);
                showMessage(errorMessage, true);
                return;
            }

            const serverLine = getSnapshotLine(result.cart, cartItemId);
            if (serverLine) state.confirmedQuantity = serverLine.quantity;

            if (hasPendingQuantity) {
                state.inFlight = false;
                updateCart(result.cart, { preserveQuantityFor: cartItemId });
                setQuantityBusy(form, false);
                if (!state.timer) void updateQuantity(form);
                return;
            }

            state.inFlight = false;
            if (state.invalid) {
                updateCart(result.cart, { preserveQuantityFor: cartItemId });
                setQuantityBusy(form, false);
                return;
            }

            state.desiredQuantity = serverLine?.quantity ?? quantity;
            updateCart(result.cart);
            setQuantityBusy(form, false);
            setQuantityStatus(form, 'Đã cập nhật.');
            showMessage(result.message || 'Giỏ hàng đã được cập nhật.', false);
            window.setTimeout(() => {
                if (form.isConnected && !state.inFlight && !state.invalid) setQuantityStatus(form, '');
            }, 1800);
        } catch (error) {
            state.inFlight = false;
            const latestQuantity = readQuantity(input);
            if (state.version !== requestVersion && latestQuantity !== null && latestQuantity !== quantity) {
                setQuantityBusy(form, false);
                if (!state.timer) void updateQuantity(form);
                return;
            }

            input.value = String(state.confirmedQuantity);
            state.desiredQuantity = state.confirmedQuantity;
            state.invalid = false;
            input.removeAttribute('aria-invalid');
            setQuantityBusy(form, false);
            const errorMessage = error instanceof Error
                ? error.message
                : 'Không thể cập nhật giỏ hàng. Hãy thử lại.';
            setQuantityStatus(form, errorMessage, true);
            showMessage(errorMessage, true);
        }
    };

    const scheduleQuantityUpdate = (form, delay = 400) => {
        const input = form.querySelector('[data-cart-quantity]');
        const state = getQuantityState(form);
        state.version++;
        if (state.timer) window.clearTimeout(state.timer);
        state.timer = null;

        const quantity = readQuantity(input);
        if (quantity === null) {
            state.invalid = true;
            input.setAttribute('aria-invalid', 'true');
            setQuantityStatus(form, 'Nhập số nguyên lớn hơn hoặc bằng 1.', true);
            return;
        }

        state.invalid = false;
        state.desiredQuantity = quantity;
        input.removeAttribute('aria-invalid');
        if (quantity === state.confirmedQuantity && !state.inFlight) {
            setQuantityStatus(form, '');
            return;
        }

        state.timer = window.setTimeout(() => {
            state.timer = null;
            void updateQuantity(form);
        }, delay);
        if (state.inFlight) setQuantityStatus(form, 'Sẽ cập nhật số lượng mới…');
    };

    const setButtonLoading = (button, loading) => {
        const spinner = button.querySelector('[data-cart-spinner]');
        button.disabled = loading;
        button.setAttribute('aria-busy', String(loading));
        spinner?.classList.toggle('d-none', !loading);
    };

    document.addEventListener('click', event => {
        const stepButton = event.target.closest('[data-cart-quantity-decrease], [data-cart-quantity-increase]');
        if (!stepButton) return;

        const form = stepButton.closest('.cart-quantity-form');
        if (!form || stepButton.disabled) return;

        const input = form.querySelector('[data-cart-quantity]');
        const state = getQuantityState(form);
        const current = readQuantity(input) ?? state.confirmedQuantity;
        const next = stepButton.hasAttribute('data-cart-quantity-increase')
            ? current + 1
            : Math.max(1, current - 1);
        if (next === current) return;

        if (state.timer) window.clearTimeout(state.timer);
        state.timer = null;
        state.version++;
        state.invalid = false;
        state.desiredQuantity = next;
        input.value = String(next);
        input.removeAttribute('aria-invalid');
        void updateQuantity(form);
    });

    document.addEventListener('input', event => {
        const input = event.target.closest('[data-cart-quantity]');
        if (!input) return;
        scheduleQuantityUpdate(input.closest('.cart-quantity-form'));
    });

    document.addEventListener('change', event => {
        const input = event.target.closest('[data-cart-quantity]');
        if (!input) return;
        scheduleQuantityUpdate(input.closest('.cart-quantity-form'), 0);
    });

    document.addEventListener('focusout', event => {
        const input = event.target.closest('[data-cart-quantity]');
        if (!input) return;
        const form = input.closest('.cart-quantity-form');
        const state = getQuantityState(form);
        if (readQuantity(input) === null) {
            if (state.timer) window.clearTimeout(state.timer);
            state.timer = null;
            state.version++;
            state.invalid = false;
            input.value = String(state.confirmedQuantity);
            input.removeAttribute('aria-invalid');
            setQuantityStatus(form, 'Giá trị không hợp lệ; đã khôi phục số lượng đã lưu.', true);
            return;
        }

        if (state.timer) window.clearTimeout(state.timer);
        state.timer = null;
        scheduleQuantityUpdate(form, 0);
    });

    document.addEventListener('submit', async event => {
        const form = event.target.closest('form[data-cart-ajax]');
        if (!form) return;

        event.preventDefault();
        if (form.dataset.cartAjax === 'update') {
            const state = getQuantityState(form);
            if (state.timer) window.clearTimeout(state.timer);
            state.timer = null;
            scheduleQuantityUpdate(form, 0);
            return;
        }

        if (form.dataset.cartAjax !== 'remove') return;
        const button = event.submitter || form.querySelector('[data-cart-remove-button]');
        if (!button || button.disabled) return;

        const row = form.closest('[data-cart-item-id]');
        const quantityForm = row?.querySelector('.cart-quantity-form');
        const quantityState = quantityForm ? getQuantityState(quantityForm) : null;
        if (quantityState?.inFlight) return;

        const nextButton = row?.nextElementSibling?.querySelector('[data-cart-remove-button]');
        const previousButton = row?.previousElementSibling?.querySelector('[data-cart-remove-button]');
        const focusAfterRemove = nextButton || previousButton;
        row.dataset.cartRemoving = 'true';
        row.querySelectorAll('[data-cart-quantity-decrease], [data-cart-quantity-increase], [data-cart-quantity]')
            .forEach(control => { control.disabled = true; });
        clearMessage();
        setButtonLoading(button, true);

        try {
            const { response, result } = await postForm(form);
            if (!response.ok) throw new Error(result.message || 'Không thể cập nhật giỏ hàng.');

            const becameEmpty = updateCart(result.cart);
            showMessage(result.message || 'Giỏ hàng đã được cập nhật.', false);
            if (becameEmpty) {
                content.querySelector('[data-cart-empty-focus]')?.focus();
            } else {
                (focusAfterRemove?.isConnected
                    ? focusAfterRemove
                    : content.querySelector('[data-cart-remove-button]'))?.focus();
            }
        } catch (error) {
            row.dataset.cartRemoving = '';
            if (quantityForm) {
                const quantityInput = quantityForm.querySelector('[data-cart-quantity]');
                const isAvailable = row.dataset.cartProductAvailable !== 'false';
                quantityInput.disabled = !isAvailable;
                quantityForm.querySelectorAll('[data-cart-quantity-decrease], [data-cart-quantity-increase]')
                    .forEach(control => { control.disabled = !isAvailable; });
            }
            setButtonLoading(button, false);
            const errorMessage = error instanceof Error
                ? error.message
                : 'Không thể cập nhật giỏ hàng. Hãy thử lại.';
            showMessage(errorMessage, true);
            button.focus();
        }
    });
})();
