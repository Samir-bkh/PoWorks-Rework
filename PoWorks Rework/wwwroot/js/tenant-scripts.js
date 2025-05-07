// JavaScript for tenant management functionality
document.addEventListener('DOMContentLoaded', function () {
    // Calculate base rate fee based on tariff type selection
    const tariffTypeSelect = document.getElementById('tariffType');
    const baseRateInput = document.getElementById('baseRate');
    const baseRateFeeInput = document.getElementById('baseRateFee');

    // Function to calculate and update the base rate fee
    function updateBaseRateFee() {
        const baseRate = parseFloat(baseRateInput.value) || 0;
        // Calculate fee based on tariff type
        const multiplier = tariffTypeSelect.value === 'Company' ? 1.1 : 1.0;
        const fee = baseRate * multiplier;
        baseRateFeeInput.value = fee.toFixed(2);
    }

    // Update the base rate fee when inputs change
    if (tariffTypeSelect && baseRateInput && baseRateFeeInput) {
        tariffTypeSelect.addEventListener('change', updateBaseRateFee);
        baseRateInput.addEventListener('input', updateBaseRateFee);
        // Initial calculation
        updateBaseRateFee();
    }

    // Form validation
    const tenantForm = document.getElementById('tenantForm');
    if (tenantForm) {
        tenantForm.addEventListener('submit', function (event) {
            // Basic validation - ensure company name is not empty
            const companyNameInput = document.getElementById('companyName');
            if (!companyNameInput.value.trim()) {
                event.preventDefault();
                alert('Company Name is required');
                companyNameInput.focus();
                return false;
            }

            // Validate numeric inputs
            const numericFields = [
                { id: 'baseRate', name: 'Base Rate' },
                { id: 'threshold1', name: 'Threshold 1' },
                { id: 'threshold1Rate', name: 'Threshold 1 Rate' },
                { id: 'threshold2', name: 'Threshold 2' },
                { id: 'threshold2Rate', name: 'Threshold 2 Rate' },
                { id: 'deposit', name: 'Deposit' }
            ];

            for (const field of numericFields) {
                const input = document.getElementById(field.id);
                if (input) {
                    const value = input.value.trim();
                    if (value && isNaN(parseFloat(value))) {
                        event.preventDefault();
                        alert(`${field.name} must be a valid number`);
                        input.focus();
                        return false;
                    }
                }
            }

            // Email validation
            const emailInput = document.getElementById('email');
            if (emailInput && emailInput.value.trim()) {
                const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
                if (!emailPattern.test(emailInput.value.trim())) {
                    event.preventDefault();
                    alert('Please enter a valid email address');
                    emailInput.focus();
                    return false;
                }
            }

            // Show loading overlay when form is submitted and validation passes
            const loadingOverlay = document.getElementById('loadingOverlay');
            if (loadingOverlay) {
                console.log('Showing loading overlay...');
                loadingOverlay.style.display = 'flex';

                // Log form data for debugging
                console.log('Form data:');
                const formData = new FormData(tenantForm);
                for (let pair of formData.entries()) {
                    console.log(pair[0] + ': ' + pair[1]);
                }
            }

            // Add a fallback to hide loading overlay if submission takes too long
            setTimeout(function () {
                if (loadingOverlay && loadingOverlay.style.display === 'flex') {
                    loadingOverlay.style.display = 'none';
                    alert('The request is taking longer than expected. Please check if your data was saved.');
                }
            }, 30000); // 30 seconds timeout

            return true;
        });
    }

    // Make table rows in search results clickable
    const searchResultRows = document.querySelectorAll('table tbody tr');
    searchResultRows.forEach(row => {
        const rowId = row.querySelector('td:first-child');
        if (rowId) {
            row.style.cursor = 'pointer';
            row.addEventListener('click', function () {
                window.location.href = `/Tenant/Management/${rowId.textContent}`;
            });
        }
    });

    // Display any server messages
    const urlParams = new URLSearchParams(window.location.search);
    const errorMessage = urlParams.get('errorMessage');
    const successMessage = urlParams.get('successMessage');

    if (errorMessage) {
        alert('Error: ' + errorMessage);
    }

    if (successMessage) {
        alert('Success: ' + successMessage);
    }

    // Add CSS for loading overlay if not already in your CSS files
    if (!document.getElementById('loadingOverlayStyles')) {
        const style = document.createElement('style');
        style.id = 'loadingOverlayStyles';
        style.textContent = `
            .loading-overlay {
                position: fixed;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                background-color: rgba(0, 0, 0, 0.5);
                display: flex;
                justify-content: center;
                align-items: center;
                z-index: 9999;
            }
            
            .spinner-container {
                background-color: white;
                padding: 20px;
                border-radius: 5px;
                text-align: center;
            }
        `;
        document.head.appendChild(style);
    }
});