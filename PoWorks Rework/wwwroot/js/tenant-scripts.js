// Client-side helpers for tenant management.
// Server-side validation remains authoritative.
document.addEventListener('DOMContentLoaded', function () {
    const tenantForm = document.getElementById('tenantForm');
    if (!tenantForm) {
        return;
    }

    tenantForm.addEventListener('submit', function (event) {
        const submitter = event.submitter;

        // Enable/disable/delete lifecycle actions only need the tenant id.
        // Do not block them because an unrelated editable field is incomplete.
        if (submitter && submitter.hasAttribute('formaction')) {
            return;
        }

        const companyNameInput = document.getElementById('companyName');
        if (companyNameInput && !companyNameInput.value.trim()) {
            event.preventDefault();
            companyNameInput.focus();
            return;
        }

        const emailInput = document.getElementById('email');
        if (emailInput && emailInput.value.trim()) {
            const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
            if (!emailPattern.test(emailInput.value.trim())) {
                event.preventDefault();
                emailInput.focus();
                return;
            }
        }

        const numericIds = [
            'baseRate',
            'threshold1',
            'threshold1Rate',
            'threshold2',
            'threshold2Rate',
            'deposit',
            'monthlyFee'
        ];

        for (const id of numericIds) {
            const input = document.getElementById(id);
            if (!input || !input.value.trim()) {
                continue;
            }

            const value = Number(input.value);
            if (!Number.isFinite(value) || value < 0) {
                event.preventDefault();
                input.focus();
                return;
            }
        }

        const threshold1 = Number(document.getElementById('threshold1')?.value || 0);
        const threshold2 = Number(document.getElementById('threshold2')?.value || 0);
        if (threshold2 < threshold1) {
            event.preventDefault();
            document.getElementById('threshold2')?.focus();
        }
    });
});
