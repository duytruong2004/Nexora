(() => {
    const toggle = document.getElementById('onlyDifferent');
    if (!toggle) return;
    const rows = [...document.querySelectorAll('[data-different]')];
    toggle.addEventListener('change', () => {
        rows.forEach(row => {
            row.hidden = toggle.checked && row.dataset.different !== 'true';
        });
    });
})();
