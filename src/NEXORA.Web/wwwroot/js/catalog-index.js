(() => {
    const sortBy = document.getElementById('sortBy');
    const filterForm = document.getElementById('catalogFilterForm');
    if (!sortBy || !filterForm) return;

    sortBy.addEventListener('change', () => filterForm.requestSubmit());
})();
