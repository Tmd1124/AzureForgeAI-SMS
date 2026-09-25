// Listens on document with capture so it catches the scroll wherever the WebView puts it
// (window or an inner container). Elements are looked up on every update because Blazor adds
// and removes the pond (search, select mode, lane changes) after observe() runs.
window.forgeLinkBubblePond = {
    handler: null,
    observe: function (pondId, miniId, headerId) {
        this.dispose();
        var ticking = false;
        var update = function () {
            ticking = false;
            var pond = document.getElementById(pondId);
            var mini = document.getElementById(miniId);
            if (!pond || !mini) {
                return;
            }
            var header = document.getElementById(headerId);
            var headerBottom = header ? header.getBoundingClientRect().bottom : 0;
            mini.style.top = headerBottom + "px";
            mini.classList.toggle("show", pond.getBoundingClientRect().bottom < headerBottom + 40);
        };
        this.handler = function () {
            if (!ticking) {
                ticking = true;
                requestAnimationFrame(update);
            }
        };
        document.addEventListener("scroll", this.handler, { passive: true, capture: true });
        update();
    },
    dispose: function () {
        if (this.handler) {
            document.removeEventListener("scroll", this.handler, { capture: true });
            this.handler = null;
        }
    }
};
