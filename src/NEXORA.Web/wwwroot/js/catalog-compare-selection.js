(() => {
    const form = document.getElementById('compareSelectionForm');
    if (!form) return;

    const checkboxes = [...document.querySelectorAll('.compare-product[form="compareSelectionForm"]')];
    const submit = document.getElementById('compareSelectionSubmit');
    const status = document.getElementById('compareSelectionStatus');

    const update = changed => {
        const selected = checkboxes.filter(item => item.checked);
        if (selected.length > 3) {
            changed.checked = false;
            window.alert('Bạn chỉ có thể so sánh tối đa 3 sản phẩm.');
        } else if (selected.length > 1) {
            const categoryIds = new Set(selected.map(item => item.dataset.categoryId));
            if (categoryIds.size > 1) {
                changed.checked = false;
                window.alert('Hãy chọn sản phẩm cùng danh mục để so sánh thông số phù hợp.');
            }
        }

        const count = checkboxes.filter(item => item.checked).length;
        status.textContent = `Đã chọn ${count}/3 sản phẩm. Chọn ít nhất 2 sản phẩm cùng danh mục.`;
        submit.disabled = count < 2;
    };

    checkboxes.forEach(item => item.addEventListener('change', () => update(item)));
    update({ checked: false });
})();
