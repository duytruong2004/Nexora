(() => {
    const gallery = document.querySelector('[data-product-gallery]');
    if (gallery) {
        const mainImage = gallery.querySelector('[data-product-gallery-main]');
        const thumbnails = Array.from(gallery.querySelectorAll('[data-product-gallery-thumbnail]'));
        const dialog = document.querySelector('[data-product-gallery-dialog]');
        const zoomImage = dialog?.querySelector('[data-product-gallery-zoom-image]');
        let activeIndex = Math.max(0, thumbnails.findIndex(button => button.getAttribute('aria-pressed') === 'true'));

        const showImage = index => {
            if (!mainImage || thumbnails.length === 0) return;
            activeIndex = (index + thumbnails.length) % thumbnails.length;
            const activeThumbnail = thumbnails[activeIndex];
            const imageUrl = activeThumbnail.dataset.imageSrc;
            const imageAlt = activeThumbnail.dataset.imageAlt || mainImage.alt;
            if (!imageUrl) return;

            mainImage.src = imageUrl;
            mainImage.alt = imageAlt;
            if (zoomImage) {
                zoomImage.src = imageUrl;
                zoomImage.alt = imageAlt;
            }

            thumbnails.forEach((button, thumbnailIndex) => {
                const selected = thumbnailIndex === activeIndex;
                button.classList.toggle('is-selected', selected);
                button.setAttribute('aria-pressed', String(selected));
            });
        };

        thumbnails.forEach((button, index) => button.addEventListener('click', () => showImage(index)));
        gallery.querySelectorAll('[data-product-gallery-step]').forEach(button => {
            button.addEventListener('click', () => {
                const step = Number(button.dataset.productGalleryStep);
                if (Number.isInteger(step)) showImage(activeIndex + step);
            });
        });

        gallery.querySelector('[data-product-gallery-zoom]')?.addEventListener('click', () => {
            if (!dialog || !mainImage || typeof dialog.showModal !== 'function') return;
            if (zoomImage) {
                zoomImage.src = mainImage.src;
                zoomImage.alt = mainImage.alt;
            }
            dialog.showModal();
        });
    }

    document.querySelectorAll('.product-quantity-control').forEach(control => {
        const input = control.querySelector('[data-product-quantity-input]');
        const form = control.closest('form');
        const addButton = form?.querySelector('[data-catalog-cart-add-button]');
        const stepButtons = Array.from(control.querySelectorAll('[data-product-quantity-step]'));
        if (!input) return;

        const minimum = Number(input.min || 1);
        const maximum = Number(input.max);
        const available = Number.isFinite(maximum) && maximum >= minimum && !input.disabled;

        const updateControls = normalize => {
            if (normalize && input.value !== '') {
                const entered = Number(input.value);
                if (Number.isFinite(entered)) {
                    input.value = String(Math.min(maximum, Math.max(minimum, Math.trunc(entered))));
                }
            }

            const value = Number(input.value);
            const valid = available && input.value !== '' && input.checkValidity();
            stepButtons.forEach(button => {
                const delta = Number(button.dataset.productQuantityStep);
                const nextValue = value + delta;
                button.disabled = !available || !Number.isFinite(value) || nextValue < minimum || nextValue > maximum;
            });
            if (addButton) {
                addButton.disabled = !valid;
                addButton.setAttribute('aria-disabled', String(!valid));
            }
        };

        stepButtons.forEach(button => button.addEventListener('click', () => {
            const current = Number(input.value);
            const delta = Number(button.dataset.productQuantityStep);
            if (!Number.isFinite(delta)) return;
            input.value = String(Math.min(maximum, Math.max(minimum, (Number.isFinite(current) ? current : minimum) + delta)));
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }));

        input.addEventListener('input', () => updateControls(false));
        input.addEventListener('change', () => updateControls(true));
        input.addEventListener('blur', () => updateControls(true));
        updateControls(false);
    });
})();
