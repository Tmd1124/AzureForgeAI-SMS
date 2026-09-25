// A plain scroll listener rather than IntersectionObserver: this WebView's engine didn't
// reliably fire intersection callbacks for a sentinel inside a flex-direction:column-reverse
// container (confirmed by observing scrollTop change with no callback), so this sidesteps that
// entirely with direct scroll-position math instead.
window.forgeLinkScrollIntoView = function (elementId) {
    var element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ block: "center" });
    }
};

window.forgeLinkInfiniteScroll = {
    handlers: new Map(),
    observe: function (containerId, dotNetRef) {
        var container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        this.dispose(containerId);

        // Guards against piling up calls while the user sits at an edge mid-scroll — reset as
        // soon as the in-flight call resolves, rather than comparing against a captured
        // scrollHeight snapshot (that approach could get permanently stuck if an earlier scroll
        // event captured the same scrollHeight the page still has once more history loads in,
        // e.g. when a page comes back shorter than expected). Separate flags per edge since
        // catching up to the newest messages and paging in older ones can each be in flight
        // independently (e.g. the older-page fetch is still resolving when the user reverses
        // direction and scrolls back down).
        var isInvokingOlder = false;
        var isInvokingNewer = false;
        var handler = function () {
            // column-reverse containers in this WebView scroll negatively toward older content
            // (confirmed empirically): scrollTop near its most negative value is "near the oldest
            // loaded message"; scrollTop near 0 is "near the newest loaded message" (the bottom).
            var maxNegativeScroll = -(container.scrollHeight - container.clientHeight);
            var nearOldestEdge = container.scrollTop <= maxNegativeScroll + 150;
            var nearNewestEdge = container.scrollTop >= -150;

            if (nearOldestEdge && !isInvokingOlder) {
                isInvokingOlder = true;
                dotNetRef.invokeMethodAsync("OnScrolledNearOldestMessage").finally(function () {
                    isInvokingOlder = false;
                });
            }
            if (nearNewestEdge && !isInvokingNewer) {
                isInvokingNewer = true;
                dotNetRef.invokeMethodAsync("OnScrolledNearNewestMessage").finally(function () {
                    isInvokingNewer = false;
                });
            }
        };

        container.addEventListener("scroll", handler, { passive: true });
        this.handlers.set(containerId, { element: container, handler: handler });
    },
    dispose: function (containerId) {
        var existing = this.handlers.get(containerId);
        if (existing) {
            existing.element.removeEventListener("scroll", existing.handler);
            this.handlers.delete(containerId);
        }
    }
};
