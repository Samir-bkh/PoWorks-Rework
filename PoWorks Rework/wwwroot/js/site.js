$(document).ready(function () {
    $("#exportBtn").off("click").on("click", function (e) {
        e.preventDefault(); 

        var format = $("input[name='exportFormat']:checked").val() || "csv"; 
        var activeOnly = $("#exportActiveOnly").is(":checked") || false;
        var includeLatest = $("#exportWithReadings").is(":checked") || false;

        var url = `/Import/ExportMeters?format=${format}&activeOnly=${activeOnly}&includeReadings=${includeLatest}`;
        
        window.location.href = url;
    });

    $("#importBtn").on("click", function () {
        alert("Import functionality will be implemented here");
    });
});

document.addEventListener('DOMContentLoaded', function () {
    if (document.getElementById('yearlyComparisonChart')) {
        console.log('Yearly chart would be initialized here');
    }

    if (document.getElementById('weeklyComparisonChart')) {
        console.log('Weekly chart would be initialized here');
    }
});