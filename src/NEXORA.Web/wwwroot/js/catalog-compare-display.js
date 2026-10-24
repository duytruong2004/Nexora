(() => {
    const toggle = document.getElementById('onlyDifferent');
    if (!toggle) return;

    const rows = [...document.querySelectorAll('[data-comparison-specification][data-different]')];
    const groupHeadings = [...document.querySelectorAll('[data-comparison-group-heading]')];

    toggle.addEventListener('change', () => {
        rows.forEach(row => {
            row.hidden = toggle.checked && row.dataset.different !== 'true';
        });

        groupHeadings.forEach(heading => {
            const groupName = heading.dataset.comparisonGroupHeading;
            const hasVisibleRows = rows.some(row =>
                row.dataset.comparisonGroup === groupName && !row.hidden);
            heading.hidden = toggle.checked && !hasVisibleRows;
        });
    });
})();
