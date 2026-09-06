//check if there's a data-type
//data-type=date //date only
$('*[data-type="date"]').datepicker({ dateFormat:"yy/mm/dd"});

//date-type=datetime //date and time
$('*[data-type="datetime"]').datetimepicker({ dateFormat: "yy/mm/dd" });

// Sidebar drawer toggle for mobile (newlayout pattern)
var sidebarOpen = false;

function isMobile() {
    return window.innerWidth < 992;
}

function toggleSidebar() {
    sidebarOpen = !sidebarOpen;
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('overlay');
    if (sidebar) sidebar.classList.toggle('open', sidebarOpen);
    if (overlay) overlay.classList.toggle('show', sidebarOpen);
}

function closeSidebar() {
    sidebarOpen = false;
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('overlay');
    if (sidebar) sidebar.classList.remove('open');
    if (overlay) overlay.classList.remove('show');
}

window.addEventListener('resize', function () {
    if (!isMobile()) {
        // Desktop: close mobile drawer state, CSS handles sidebar visibility
        closeSidebar();
    }
});

$(function () {
    // Close sidebar drawer when a nav link is clicked on mobile
    $(document).on('click', '.nav-item-link', function () {
        if (isMobile()) {
            closeSidebar();
        }
    });
});
