// wwwroot/js/site.js
$(document).ready(function () {
    // Export button click handler
    $("#exportBtn").on("click", function () {
        alert("Export functionality will be implemented here");
    });

    // Import button click handler
    $("#importBtn").on("click", function () {
        alert("Import functionality will be implemented here");
    });
});

$(document).ready(function () {
    $("#retrieveBtn").on("click", function () {
        var billNumber = $("#BillNumber").val();
        if (billNumber) {
            // In a real application, this would make an AJAX call to retrieve the bill details
            // For this demo, we'll just submit the form
            var form = $("<form></form>").attr({
                method: "post",
                action: "/Payments/RetrieveBill"
            }).appendTo("body");

            $("<input>").attr({
                type: "hidden",
                name: "billNumber",
                value: billNumber
            }).appendTo(form);

            form.submit();
        } else {
            alert("Please enter a bill number.");
        }
    });
});

// This would be added to site.js or a separate charts.js file
document.addEventListener('DOMContentLoaded', function () {
    // Check if charts containers exist
    if (document.getElementById('yearlyComparisonChart')) {
        // Initialize Yearly Comparison Chart
        // This is just example code - in a real implementation you would use a library like Chart.js
        console.log('Yearly chart would be initialized here');

        // Yearly data would be passed from the controller, something like:
        // var yearlyData = @Json.Serialize(Model.ConsumptionData.YearlyData);
    }

    if (document.getElementById('weeklyComparisonChart')) {
        // Initialize Weekly Comparison Chart
        console.log('Weekly chart would be initialized here');

        // Weekly data would be passed from the controller, something like:
        // var weeklyData = @Json.Serialize(Model.ConsumptionData.WeeklyData);
    }
});