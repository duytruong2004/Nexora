(() => {
    const picker = document.querySelector("[data-promotion-product-picker]");
    if (!picker) return;

    const checkboxes = Array.from(picker.querySelectorAll('input[name="SelectedProductIds"]'));
    const toggleButton = picker.querySelector("[data-select-all-products]");
    const selectedCount = picker.querySelector("[data-selected-product-count]");
    if (!toggleButton || !selectedCount) return;

    const updateSelectionState = () => {
        const selected = checkboxes.filter((checkbox) => checkbox.checked).length;
        const allSelected = checkboxes.length > 0 && selected === checkboxes.length;

        selectedCount.textContent = `Đã chọn ${selected}/${checkboxes.length} sản phẩm`;
        toggleButton.textContent = allSelected ? "Bỏ chọn tất cả" : "Chọn tất cả";
        toggleButton.setAttribute("aria-pressed", String(allSelected));
        toggleButton.disabled = checkboxes.length === 0;
    };

    toggleButton.addEventListener("click", () => {
        const selectAll = checkboxes.some((checkbox) => !checkbox.checked);
        checkboxes.forEach((checkbox) => {
            checkbox.checked = selectAll;
        });
        updateSelectionState();
    });

    checkboxes.forEach((checkbox) => checkbox.addEventListener("change", updateSelectionState));
    updateSelectionState();
})();
